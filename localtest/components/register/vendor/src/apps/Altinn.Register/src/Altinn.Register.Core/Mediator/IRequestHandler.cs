using Altinn.Authorization.ProblemDetails;

namespace Altinn.Register.Core.Mediator;

/// <summary>
/// A request handler.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestHandler<TRequest, TResponse>
    where TRequest : notnull, IRequest<TResponse>
    where TResponse : notnull
{
    /// <summary>
    /// Processes the request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The response, or an error.</returns>
    public ValueTask<Result<TResponse>> Handle(TRequest request, CancellationToken cancellationToken);
}
