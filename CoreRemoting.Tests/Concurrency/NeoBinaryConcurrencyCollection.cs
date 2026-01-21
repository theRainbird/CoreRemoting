using System;
using System.Collections.Generic;
using Xunit;

namespace CoreRemoting.Tests.Concurrency
{
	/// <summary>
	/// Collection definition for NeoBinary concurrency tests.
	/// </summary>
	[CollectionDefinition("NeoBinaryConcurrency")]
	public class NeoBinaryConcurrencyCollection : ICollectionFixture<NeoBinaryConcurrencyFixture>
	{
		// This class has no code, and is never created.
		// Its purpose is to be the place to apply [CollectionDefinition] and 
		// ICollectionFixture<> interfaces.
	}
}