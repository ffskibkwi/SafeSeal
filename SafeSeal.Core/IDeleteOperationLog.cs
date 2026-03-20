namespace SafeSeal.Core;

public interface IDeleteOperationLog
{
    void Write(DeleteOperationEvent operationEvent);
}
