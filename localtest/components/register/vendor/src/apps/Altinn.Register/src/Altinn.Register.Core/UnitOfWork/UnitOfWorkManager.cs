using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Register.Core.UnitOfWork;

/// <summary>
/// Implementation of <see cref="IUnitOfWorkManager"/>.
/// </summary>
internal class UnitOfWorkManager
    : IUnitOfWorkManager
{
    private readonly Impl _impl;
    private readonly IServiceProvider _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnitOfWorkManager"/> class.
    /// </summary>
    public UnitOfWorkManager(
        Impl cache,
        IServiceProvider services)
    {
        _impl = cache;
        _services = services;
    }

    /// <inheritdoc/>
    public ValueTask<IUnitOfWork> CreateAsync(
        ReadOnlySpan<KeyValuePair<string, object?>> tags = default,
        ReadOnlySpan<ActivityLink> links = default,
        CancellationToken cancellationToken = default,
        [CallerMemberName] string activityName = "")
        => _impl.CreateAsync(RegisterTelemetry.StartActivity(activityName, ActivityKind.Internal, tags: tags, links: links), _services, cancellationToken);

    /// <summary>
    /// The actual unit-of-work implementation.
    /// </summary>
    internal sealed class Impl
    {
        private readonly ImmutableArray<IUnitOfWorkParticipantFactory> _participants;
        private readonly FrozenDictionary<Type, Func<UnitOfWork, object>> _services;
        private readonly int _serviceCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="Impl"/> class.
        /// </summary>
        public Impl(
            IEnumerable<IUnitOfWorkParticipantFactory> participants,
            IEnumerable<IUnitOfWorkServiceFactory> services)
        {
            _participants = participants.ToImmutableArray();

            var builder = new List<KeyValuePair<Type, Func<UnitOfWork, object>>>();
            for (var i = 0; i < _participants.Length; i++)
            {
                var participantFactory = _participants[i];
                foreach (var type in participantFactory.ServiceTypes)
                {
                    builder.Add(KeyValuePair.Create(type, UnitOfWork.CreateParticipantLookup(i, type)));
                }
            }

            var serviceIndex = 0;
            foreach (var serviceFactory in services)
            {
                var anyType = false;
                foreach (var iface in serviceFactory.GetType().GetInterfaces())
                {
                    if (IsServiceFactoryInterface(iface, out var serviceType))
                    {
                        anyType = true;
                        builder.Add(KeyValuePair.Create(serviceType, UnitOfWork.CreateServiceActivator(serviceIndex++, serviceFactory, serviceType)));
                    }
                }

                if (!anyType)
                {
                    ThrowHelper.ThrowInvalidOperationException($"Service factory {serviceFactory.GetType()} does not implement IUnitOfWorkServiceFactory<T> for any T");
                }
            }

            _services = builder.ToFrozenDictionary();
            _serviceCount = serviceIndex;

            static bool IsServiceFactoryInterface(Type iface, [NotNullWhen(true)] out Type? serviceType)
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IUnitOfWorkServiceFactory<>))
                {
                    serviceType = iface.GetGenericArguments()[0];
                    return true;
                }

                serviceType = null;
                return false;
            }
        }

        /// <summary>
        /// Creates a new unit of work.
        /// </summary>
        /// <param name="activity">An optional <see cref="Activity"/>.</param>
        /// <param name="serviceProvider">A <see cref="IServiceProvider"/>.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
        /// <returns>A <see cref="IUnitOfWork"/>.</returns>
        public ValueTask<IUnitOfWork> CreateAsync(
            Activity? activity,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            List<Task<(IUnitOfWorkParticipant Participant, int Index)>>? pending = null;
            var builder = ImmutableArray.CreateBuilder<IUnitOfWorkParticipant?>(_participants.Length);
            var handle = new Handle();

            for (var i = 0; i < _participants.Length; i++)
            {
                var factory = _participants[i];
                var participant = factory.Create(handle, serviceProvider, cancellationToken);
                if (participant.IsCompleted)
                {
                    builder.Add(participant.Result);
                }
                else
                {
                    builder.Add(null);
                    pending ??= new(_participants.Length);
                    pending.Add(CreatePendingTask(participant, i));
                }
            }

            if (pending is not null)
            {
                return WaitForPending(activity, pending, builder, handle, this, serviceProvider, cancellationToken);
            }

            return new(Create(activity, builder, handle, this, serviceProvider));

            static IUnitOfWork Create(
                Activity? activity,
                ImmutableArray<IUnitOfWorkParticipant?>.Builder participants,
                Handle handle,
                Impl impl,
                IServiceProvider serviceProvider)
            {
                Debug.Assert(participants.Count == participants.Capacity);
                Debug.Assert(participants.All(p => p is not null));

                return new UnitOfWork(handle, activity, serviceProvider, participants.MoveToImmutable()!, impl);
            }

            static async ValueTask<IUnitOfWork> WaitForPending(
                Activity? activity,
                List<Task<(IUnitOfWorkParticipant Participant, int Index)>> pending,
                ImmutableArray<IUnitOfWorkParticipant?>.Builder builder,
                Handle handle,
                Impl impl,
                IServiceProvider serviceProvider,
                CancellationToken cancellationToken)
            {
                var completed = await Task.WhenAll(pending);
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var (participant, index) in completed)
                {
                    builder[index] = participant;
                }

                return Create(activity, builder, handle, impl, serviceProvider);
            }

            static async Task<(IUnitOfWorkParticipant Participant, int Index)> CreatePendingTask(
                ValueTask<IUnitOfWorkParticipant> pendingParticipant,
                int index)
            {
                var participant = await pendingParticipant;
                return (participant, index);
            }
        }

        /// <summary>
        /// Gets a service from the unit of work.
        /// </summary>
        /// <param name="serviceProvider">The fallback <see cref="IServiceProvider"/>.</param>
        /// <param name="unitOfWork">The <see cref="UnitOfWork"/>.</param>
        /// <param name="serviceType">The service type.</param>
        /// <returns>A service of type <paramref name="serviceType"/>.</returns>
        private object? GetService(IServiceProvider serviceProvider, UnitOfWork unitOfWork, Type serviceType)
        {
            if (serviceType == typeof(IUnitOfWorkHandle))
            {
                return unitOfWork.Handle;
            }

            if (serviceType == typeof(IServiceProvider))
            {
                return unitOfWork.ServiceProvider;
            }

            if (_services.TryGetValue(serviceType, out var activator))
            {
                return activator(unitOfWork);
            }

            return serviceProvider.GetService(serviceType);
        }

        /// <summary>
        /// Gets a required service from the unit of work.
        /// </summary>
        /// <param name="serviceProvider">The fallback <see cref="IServiceProvider"/>.</param>
        /// <param name="unitOfWork">The <see cref="UnitOfWork"/>.</param>
        /// <param name="serviceType">The service type.</param>
        /// <returns>A service of type <paramref name="serviceType"/>.</returns>
        private object GetRequiredService(IServiceProvider serviceProvider, UnitOfWork unitOfWork, Type serviceType)
        {
            if (serviceType == typeof(IUnitOfWorkHandle))
            {
                return unitOfWork.Handle;
            }

            if (serviceType == typeof(IServiceProvider))
            {
                return unitOfWork.ServiceProvider;
            }

            if (_services.TryGetValue(serviceType, out var activator))
            {
                return activator(unitOfWork);
            }

            return serviceProvider.GetRequiredService(serviceType);
        }

        /// <summary>
        /// Rents a buffer for services.
        /// </summary>
        /// <returns>A rented <see langword="object"/> array.</returns>
        private object?[] RentServices()
        {
            var services = ArrayPool<object?>.Shared.Rent(_serviceCount);

            services.AsSpan().Clear();

            return services;
        }

        /// <summary>
        /// Releases a buffer for services.
        /// </summary>
        /// <param name="services">An <see langword="object"/> array previously returned from <see cref="RentServices"/>.</param>
        private async ValueTask DisposeServices(object?[] services)
        {
            for (var i = 0; i < _serviceCount; i++)
            {
                switch (services[i])
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync();
                        break;

                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }

            ArrayPool<object?>.Shared.Return(services, clearArray: true);
        }

        private sealed class Handle
            : IUnitOfWorkHandle
        {
            private readonly Lock _lock = new();
            private readonly CancellationTokenSource _cts = new();
            private UnitOfWorkStatus _status = UnitOfWorkStatus.Active;

#if DEBUG
            private readonly StackTrace _createdStackTrace = new(skipFrames: 1);
            private StackTrace? _closedStackTrace = null;

            public void ThrowIfNotDisposed(string displayName)
            {
                lock (_lock)
                {
                    if (_status != UnitOfWorkStatus.Disposed)
                    {
                        Debug.Fail(
                            $"""
                            Unit of work was not disposed: {displayName}
                            {_createdStackTrace}
                            """);
                    }
                }
            }
#endif

            public UnitOfWorkStatus Status
            {
                get
                {
                    lock (_lock)
                    {
                        return _status;
                    }
                }
            }

            public CancellationToken Token
                => _cts.Token;

            /// <summary>
            /// Marks the unit of work as disposed.
            /// </summary>
            /// <returns>
            /// <see langword="true"/> if the unit of work was disposed by this call; otherwise <see langword="false"/>.
            /// </returns>
            public bool MarkDisposed()
            {
                lock (_lock)
                {
                    if (_status == UnitOfWorkStatus.Disposed)
                    {
                        return false;
                    }

                    _status = UnitOfWorkStatus.Disposed;
                    _cts.Dispose();
                    return true;
                }
            }

#if DEBUG
            void IUnitOfWorkHandle.ThrowIfCompleted()
            {
                lock (_lock)
                {
                    if (_status == UnitOfWorkStatus.Disposed)
                    {
                        ThrowHelper.ThrowObjectDisposedException(nameof(IUnitOfWork), "The unit of work has been disposed.");
                    }

                    if (_status != UnitOfWorkStatus.Active)
                    {
                        ThrowHelper.ThrowInvalidOperationException(
                            $"""
                            The unit of work has been completed.

                            ==================================================
                            The unit of work was closed at: {_closedStackTrace}.

                            ==================================================
                            The unit of work was created at: {_createdStackTrace}.
                            """);
                    }
                }
            }
#endif

            public void BeginTransitionTo(UnitOfWorkStatus status)
            {
#if DEBUG
                Debug.Assert(status is UnitOfWorkStatus.Committed or UnitOfWorkStatus.RolledBack);
                var stackTrace = new StackTrace(skipFrames: 2);
#endif

                lock (_lock)
                {
                    if (_status == UnitOfWorkStatus.Disposed)
                    {
                        ThrowHelper.ThrowObjectDisposedException(nameof(UnitOfWork));
                    }

                    if (_status == UnitOfWorkStatus.Committed)
                    {
                        ThrowHelper.ThrowInvalidOperationException("Unit of work has already been committed");
                    }

                    if (_status == UnitOfWorkStatus.RolledBack)
                    {
                        ThrowHelper.ThrowInvalidOperationException("Unit of work has already been rolled back");
                    }

                    _status = status;

#if DEBUG
                    _closedStackTrace = stackTrace;
#endif
                }
            }

            public Task CompleteTransition()
            {
                lock (_lock)
                {
                    if (_status is not (UnitOfWorkStatus.Committed or UnitOfWorkStatus.RolledBack))
                    {
                        return Task.CompletedTask;
                    }
                }

                return _cts.CancelAsync();
            }
        }

        private sealed class UnitOfWorkServiceProvider
            : IServiceProvider
            , ISupportRequiredService
        {
            private readonly UnitOfWork _uow;
            private readonly IServiceProvider _fallback;

            public UnitOfWorkServiceProvider(UnitOfWork uow, IServiceProvider fallback)
            {
                _uow = uow;
                _fallback = fallback;
            }

            /// <inheritdoc/>
            public object? GetService(Type serviceType)
                => _uow.GetService(serviceType, _fallback);

            /// <inheritdoc/>
            public object GetRequiredService(Type serviceType)
                => _uow.GetRequiredService(serviceType, _fallback);
        }

        private sealed class UnitOfWork
            : IUnitOfWork
        {
            internal static Func<UnitOfWork, object> CreateParticipantLookup(int index, Type serviceType)
                => (Func<UnitOfWork, object>)_createParticipantLookupGeneric.MakeGenericMethod(serviceType).Invoke(null, [index])!;

            private static MethodInfo _createParticipantLookupGeneric = typeof(UnitOfWork).GetMethod(nameof(CreateParticipantLookupGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

            private static Func<UnitOfWork, object> CreateParticipantLookupGeneric<TService>(int index)
                where TService : class
            {
                return self =>
                {
                    var participant = self._participants[index];

                    Debug.Assert(participant is IUnitOfWorkParticipant<TService>);

                    return ((IUnitOfWorkParticipant<TService>)participant).Service;
                };
            }

            internal static Func<UnitOfWork, object> CreateServiceActivator(int index, IUnitOfWorkServiceFactory factory, Type serviceType)
                => (Func<UnitOfWork, object>)_createServiceActivatorGeneric.MakeGenericMethod(serviceType).Invoke(null, [index, factory])!;

            private static MethodInfo _createServiceActivatorGeneric = typeof(UnitOfWork).GetMethod(nameof(CreateServiceActivatorGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;

            private static Func<UnitOfWork, object> CreateServiceActivatorGeneric<TService>(int index, IUnitOfWorkServiceFactory<TService> factory)
                where TService : class
            {
                return self =>
                {
                    ref var serviceSlot = ref self._services[index];

                    serviceSlot ??= factory.Create(self.ServiceProvider);

                    return serviceSlot!;
                };
            }

            private readonly Handle _handle;
            private readonly Activity? _activity;
            private readonly UnitOfWorkServiceProvider _serviceProvider;
            private readonly ImmutableArray<IUnitOfWorkParticipant> _participants;
            private readonly Impl _impl;
            private object?[] _services;

            public UnitOfWork(
                Handle handle,
                Activity? activity,
                IServiceProvider serviceProvider,
                ImmutableArray<IUnitOfWorkParticipant> participants,
                Impl impl)
            {
                _handle = handle;
                _activity = activity;
                _participants = participants;
                _impl = impl;

                _services = _impl.RentServices();
                _serviceProvider = new UnitOfWorkServiceProvider(this, serviceProvider);
            }

            public Handle Handle
                => _handle;

            public UnitOfWorkStatus Status
                => _handle.Status;

            public CancellationToken Token
                => _handle.Token;

            internal UnitOfWorkServiceProvider ServiceProvider
                => _serviceProvider;

#if DEBUG
            ~UnitOfWork()
            {
                _handle.ThrowIfNotDisposed(_activity?.DisplayName ?? "<no display name>");
            }
#endif

            public ValueTask DisposeAsync()
            {
#if DEBUG
                GC.SuppressFinalize(this);
#endif

                if (!_handle.MarkDisposed())
                {
                    // already disposed
                    return ValueTask.CompletedTask;
                }

                _activity?.Dispose();
                return DisposeCore();

                async ValueTask DisposeCore()
                {
                    foreach (var participant in _participants)
                    {
                        await participant.DisposeAsync();
                    }

                    await _impl.DisposeServices(_services);
                    _services = null!;
                }
            }

            internal object? GetService(Type serviceType, IServiceProvider fallback)
            {
                ((IUnitOfWorkHandle)_handle).ThrowIfCompleted();

                return _impl.GetService(fallback, this, serviceType);
            }

            internal object GetRequiredService(Type serviceType, IServiceProvider fallback)
            {
                ((IUnitOfWorkHandle)_handle).ThrowIfCompleted();

                return _impl.GetRequiredService(fallback, this, serviceType);
            }

            object? IServiceProvider.GetService(Type serviceType)
                => _serviceProvider.GetService(serviceType);

            object ISupportRequiredService.GetRequiredService(Type serviceType)
                => _serviceProvider.GetRequiredService(serviceType);

            public ValueTask CommitAsync(CancellationToken cancellationToken = default)
            {
                _handle.BeginTransitionTo(UnitOfWorkStatus.Committed);
                return Inner(_handle, _activity, _participants, cancellationToken);

                static async ValueTask Inner(Handle handle, Activity? activity, ImmutableArray<IUnitOfWorkParticipant> participants, CancellationToken cancellationToken)
                {
                    foreach (var participant in participants)
                    {
                        await participant.CommitAsync(cancellationToken);
                    }

                    activity?.SetStatus(ActivityStatusCode.Ok, description: "Committed");
                    await handle.CompleteTransition();
                }
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
            {
                _handle.BeginTransitionTo(UnitOfWorkStatus.RolledBack);
                return Inner(_handle, _activity, _participants, cancellationToken);

                static async ValueTask Inner(Handle handle, Activity? activity, ImmutableArray<IUnitOfWorkParticipant> participants, CancellationToken cancellationToken)
                {
                    foreach (var participant in participants)
                    {
                        await participant.RollbackAsync(cancellationToken);
                    }

                    activity?.SetStatus(ActivityStatusCode.Ok, description: "Rolled back");
                    await handle.CompleteTransition();
                }
            }
        }
    }
}
