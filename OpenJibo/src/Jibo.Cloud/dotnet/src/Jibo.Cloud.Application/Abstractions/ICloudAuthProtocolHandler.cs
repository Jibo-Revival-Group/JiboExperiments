using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Abstractions;

public interface ICloudAuthProtocolHandler
{
    ProtocolDispatchResult HandleAccount(string operation, ProtocolEnvelope envelope);
    ProtocolDispatchResult HandleNotification(string operation, ProtocolEnvelope envelope);
}