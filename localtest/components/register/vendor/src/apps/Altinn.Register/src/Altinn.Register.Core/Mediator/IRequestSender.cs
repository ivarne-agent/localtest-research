using Altinn.Authorization.ProblemDetails;

namespace Altinn.Register.Core.Mediator;

/// <summary>
/// An interface for sending requests through a mediator. This is used by application code to send requests without having to know about the underlying handlers or API sources.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestSender<TRequest, TResponse>
    where TRequest : notnull, IRequest<TResponse>
    where TResponse : notnull
{
    /// <summary>
    /// Sends the specified request and returns the result asynchronously.
    /// </summary>
    /// <param name="request">The request object containing the data to be processed. Cannot be null.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A value task representing the asynchronous operation. The task result contains the outcome of processing the
    /// request.</returns>
    public ValueTask<Result<TResponse>> Send(TRequest request, CancellationToken cancellationToken = default);
}
