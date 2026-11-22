using System;

namespace Feeder.ToolHost.Common.Model
{
public class ResponseData<T> : ResponseData, IRequestID
{
	public T? Value { get; set; }

	public ResponseData()
	{
	}

	public ResponseData(string requestId, ResponseStatus status)
	{
		base.RequestID = requestId ?? throw new ArgumentNullException("requestId");
		base.Status = status;
	}

	public new ResponseData<T> SetRequestID(string requestId)
	{
		base.SetRequestID(requestId);
		return this;
	}

	public new static ResponseData<T> Success(string requestId, string? message = null)
	{
		return new ResponseData<T>(requestId, ResponseStatus.Success)
		{
			Message = message
		};
	}

	public new static ResponseData<T> Error(string requestId, string? message = null)
	{
		return new ResponseData<T>(requestId, ResponseStatus.Error)
		{
			Message = message
		};
	}

	public new static ResponseData<T> Processing(string requestId, string? message = null)
	{
		return new ResponseData<T>(requestId, ResponseStatus.Processing)
		{
			Message = message
		};
	}
}
public class ResponseData : IRequestID
{
	public string RequestID { get; set; } = string.Empty;

	public ResponseStatus Status { get; set; }

	public string? Message { get; set; }

	public ResponseData()
	{
	}

	public ResponseData(string requestId, ResponseStatus status)
	{
		RequestID = requestId ?? throw new ArgumentNullException("requestId");
		Status = status;
	}

	public virtual ResponseData SetRequestID(string requestId)
	{
		RequestID = requestId;
		return this;
	}

	public static ResponseData Success(string requestId, string? message = null)
	{
		return new ResponseData(requestId, ResponseStatus.Success)
		{
			Message = message
		};
	}

	public static ResponseData Error(string requestId, string? message = null)
	{
		return new ResponseData(requestId, ResponseStatus.Error)
		{
			Message = message
		};
	}

	public static ResponseData Processing(string requestId, string? message = null)
	{
		return new ResponseData(requestId, ResponseStatus.Processing)
		{
			Message = message
		};
	}
}
}
