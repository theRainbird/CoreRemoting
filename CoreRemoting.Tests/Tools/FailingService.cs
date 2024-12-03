using System;

namespace CoreRemoting.Tests.Tools;

public class FailingService : IFailingService
{
	public FailingService()
	{
		throw new NotImplementedException();
	}

	public void Hello()
	{
	}
}
