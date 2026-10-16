namespace Altinn.Register.Core.Mediator;

/// <summary>
/// Marker interface for a request with a response, used by the <see cref="RegisterMediator"/> to identify requests and their corresponding response types.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequest<TResponse>
{
}
