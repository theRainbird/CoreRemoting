using System;
using System.Data;
using System.Threading.Tasks;
using CoreRemoting.Tests.ExternalTypes;

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
