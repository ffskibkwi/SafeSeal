using System.Diagnostics;

namespace SafeSeal.Core;

public sealed class TraceDeleteOperationLog : IDeleteOperationLog
{
    public void Write(DeleteOperationEvent operationEvent)
    {
        ArgumentNullException.ThrowIfNull(operationEvent);

        Trace.TraceInformation(
            "DeleteOp doc={0} op={1} phase={2} result={3} utc={4:o} message={5}",
            operationEvent.DocumentId,
            operationEvent.OperationId,
            operationEvent.Phase,
            operationEvent.Result,
            operationEvent.Utc,
            operationEvent.Message);
    }
}
