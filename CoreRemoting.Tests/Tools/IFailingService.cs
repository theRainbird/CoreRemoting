using System;
using System.Data;
using System.Threading.Tasks;
using CoreRemoting.Tests.ExternalTypes;

namespace CoreRemoting.Tests.Tools;

public interface IFailingService
{
	void Hello();
}
