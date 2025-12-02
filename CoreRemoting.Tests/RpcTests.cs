using System;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Authentication;
using CoreRemoting.Channels;
using CoreRemoting.RemoteDelegates;
using CoreRemoting.Serialization;
using CoreRemoting.Tests.ExternalTypes;
using CoreRemoting.Tests.Tools;
using CoreRemoting.Threading;
using CoreRemoting.Toolbox;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests;

[Collection("CoreRemoting")]
public class RpcTests : IClassFixture<ServerFixture>
{
	private readonly ServerFixture _serverFixture;
	private readonly ITestOutputHelper _testOutputHelper;
	private bool _remoteServiceCalled;

	protected virtual IServerChannel ServerChannel => null;

	protected virtual IClientChannel ClientChannel => null;

	public RpcTests(ServerFixture serverFixture, ITestOutputHelper testOutputHelper)
	{
		_serverFixture = serverFixture;
		_testOutputHelper = testOutputHelper;

		_serverFixture.TestService.TestMethodFake = arg =>
		{
			_remoteServiceCalled = true;
			return arg;
		};

		_serverFixture.Start(ServerChannel);

		// setup event handler invoker
		EventStub.DelegateInvoker = DelegateInvoker;
	}

	/// <summary>
	/// Gets the delegate invoker to be used for event handler tests.
	/// </summary>
	protected virtual IDelegateInvoker DelegateInvoker => new SafeDynamicInvoker();

	[Fact]
	public void ValidationSyncContext_is_installed()
	{
		using var ctx = ValidationSyncContext.Install();

		Assert.IsType<ValidationSyncContext>(SynchronizationContext.Current);
	}

	private void CheckServerErrorCount()
	{
		try
		{
			Assert.Equal(0, _serverFixture.ServerErrorCount);
		}
		finally
		{
			if (_serverFixture.ServerErrorCount != 0)
			{
				Console.WriteLine($"LastServerError: {_serverFixture.LastServerError}");
			}
		}
	}

	[Fact]
	public void Call_on_Proxy_should_be_invoked_on_remote_service()
	{
		void ClientAction()
		{
			using var ctx = ValidationSyncContext.Install();

			try
			{
				var stopWatch = new Stopwatch();
				stopWatch.Start();

				using var client = new RemotingClient(new ClientConfig()
				{
					ConnectionTimeout = 0,
					MessageEncryption = false,
					Channel = ClientChannel,
					ServerPort = _serverFixture.Server.Config.NetworkPort,
				});

				stopWatch.Stop();
				_testOutputHelper.WriteLine($"Creating client took {stopWatch.ElapsedMilliseconds} ms");
				stopWatch.Reset();
				stopWatch.Start();

				client.Connect();

				stopWatch.Stop();
				_testOutputHelper.WriteLine($"Establishing connection took {stopWatch.ElapsedMilliseconds} ms");
				stopWatch.Reset();
				stopWatch.Start();

				var proxy = client.CreateProxy<ITestService>();

				stopWatch.Stop();
				_testOutputHelper.WriteLine($"Creating proxy took {stopWatch.ElapsedMilliseconds} ms");
				stopWatch.Reset();
				stopWatch.Start();

				var result = proxy.TestMethod("test");

				stopWatch.Stop();
				_testOutputHelper.WriteLine($"Remote method invocation took {stopWatch.ElapsedMilliseconds} ms");
				stopWatch.Reset();
				stopWatch.Start();

				var result2 = proxy.TestMethod("test");

				stopWatch.Stop();
				_testOutputHelper.WriteLine($"Second remote method invocation took {stopWatch.ElapsedMilliseconds} ms");

				Assert.Equal("test", result);
				Assert.Equal("test", result2);

				proxy.MethodWithOutParameter(out int methodCallCount);

				Assert.Equal(1, methodCallCount);
			}
			catch (Exception e)
			{
				_testOutputHelper.WriteLine(e.ToString());
				throw;
			}
		}

		var clientThread = new Thread(ClientAction);
		clientThread.Start();
		clientThread.Join();

		Assert.True(_remoteServiceCalled);
		CheckServerErrorCount();
	}

	[Fact]
	public void Call_on_Proxy_should_be_invoked_on_remote_service_with_MessageEncryption()
	{
		_serverFixture.Server.Config.MessageEncryption = true;
		// Use a smaller RSA key size for tests to avoid very slow key generation on some platforms
		var oldKeySize = _serverFixture.Server.Config.KeySize;
		_serverFixture.Server.Config.KeySize = 1024;

		void ClientAction()
		{
			using var ctx = ValidationSyncContext.Install();

			try
			{
				var stopWatch = new Stopwatch();
				stopWatch.Start();

				using var client = new RemotingClient(new ClientConfig()
				{
					ConnectionTimeout = 0,
					Channel = ClientChannel,
					ServerPort = _serverFixture.Server.Config.NetworkPort,
					MessageEncryption = true,
					KeySize = 1024,
				});

				stopWatch.Stop();
				_testOutputHelper.WriteLine($"Creating client took {stopWatch.ElapsedMilliseconds} ms");
				stopWatch.Reset();
				stopWatch.Start();

				client.Connect();

				stopWatch.Stop();
				_testOutputHelper.WriteLine($"Establishing connection took {stopWatch.ElapsedMilliseconds} ms");
				stopWatch.Reset();
				stopWatch.Start();

				var proxy = client.CreateProxy<ITestService>();

				stopWatch.Stop();
				_testOutputHelper.WriteLine($"Creating proxy took {stopWatch.ElapsedMilliseconds} ms");
				stopWatch.Reset();
				stopWatch.Start();

				var result = proxy.TestMethod("test");

				stopWatch.Stop();
				_testOutputHelper.WriteLine($"Remote method invocation took {stopWatch.ElapsedMilliseconds} ms");
				stopWatch.Reset();
				stopWatch.Start();

				var result2 = proxy.TestMethod("test");

				stopWatch.Stop();
				_testOutputHelper.WriteLine($"Second remote method invocation took {stopWatch.ElapsedMilliseconds} ms");

				Assert.Equal("test", result);
				Assert.Equal("test", result2);
			}
			catch (Exception e)
			{
				_testOutputHelper.WriteLine(e.ToString());
				throw;
			}
			finally
			{
				// restore server key size for other tests
				_serverFixture.Server.Config.KeySize = oldKeySize;
			}
		}

		var clientThread = new Thread(ClientAction);
		clientThread.Start();
		clientThread.Join();

		_serverFixture.Server.Config.MessageEncryption = false;

		Assert.True(_remoteServiceCalled);
		CheckServerErrorCount();
	}

	[Fact]
	public async Task Delegate_invoked_on_server_should_callback_client()
	{
		string argumentFromServer = null;
		var serverCalled = new TaskCompletionSource<bool>();

		void ClientAction()
		{
			using var ctx = ValidationSyncContext.Install();

			try
			{
				using var client = new RemotingClient(
					new ClientConfig()
					{
						ConnectionTimeout = 0,
						Channel = ClientChannel,
						MessageEncryption = false,
						ServerPort = _serverFixture.Server.Config.NetworkPort,
					});

				client.Connect();

				var proxy = client.CreateProxy<ITestService>();
				proxy.TestMethodWithDelegateArg(arg =>
				{
					argumentFromServer = arg;
					serverCalled.TrySetResult(true);
				});
			}
			catch (Exception e)
			{
				_testOutputHelper.WriteLine(e.ToString());
				throw;
			}
		}

		var clientThread = new Thread(ClientAction);
		clientThread.Start();
		clientThread.Join();

		await serverCalled.Task.Timeout(1);
		Assert.Equal("test", argumentFromServer);
		CheckServerErrorCount();
	}

	[Fact]
	public async Task Call_on_Proxy_should_be_executed_asynchronously()
	{
		var longRunnigMethodStarted = new AsyncCounter();

		void BeforeCall(object sender, ServerRpcContext e)
		{
			if (e.MethodCallMessage.MethodName == "LongRunnigTestMethod")
				longRunnigMethodStarted++;
		}

		try
		{
			_serverFixture.Server.BeforeCall += BeforeCall;
			using var client = new RemotingClient(
				new ClientConfig()
				{
					ConnectionTimeout = 0,
					Channel = ClientChannel,
					MessageEncryption = false,
					ServerPort = _serverFixture.Server.Config.NetworkPort,
				});

			client.Connect();

			var proxy = client.CreateProxy<ITestService>();
			var longRun = Task.Run(() => proxy.LongRunnigTestMethod(2000));

			// wait until long-running test method is started on server
			await longRunnigMethodStarted.WaitForValue(1).Timeout(2);

			// try running another RPC call in parallel
			proxy.TestMethod("x");

			// if a client is disposed before LongRunningTestMethod rpc result is received,
			// _serverFixture gets its error counter incremented which breaks other tests
			await longRun;
		}
		catch (Exception e)
		{
			_testOutputHelper.WriteLine(e.ToString());
			throw;
		}
		finally
		{
			_serverFixture.Server.BeforeCall -= BeforeCall;
			_serverFixture.ServerErrorCount = 0;
		}
	}

	[Theory]
	[InlineData("TestService_Singleton_Service")]
	[InlineData("TestService_Singleton_Factory")]
	[InlineData("TestService_SingleCall_Service")]
	[InlineData("TestService_SingleCall_Factory")]
	[InlineData("TestService_Scoped_Service")]
	[InlineData("TestService_Scoped_Factory")]
	public void Component_lifetime_matches_the_expectation(string serviceName)
	{
		using var ctx = ValidationSyncContext.Install();
		using var client = new RemotingClient(
			new ClientConfig()
			{
				ConnectionTimeout = 0,
				SendTimeout = 0,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort,
			});

		client.Connect();

		var proxy = client.CreateProxy<ITestService>(serviceName);

		// check if service lifetime matches the expectations
		proxy.SaveLastInstance();

		// singleton component should have the same instance
		var sameInstance = serviceName.Contains("Singleton");
		Assert.Equal(sameInstance, proxy.CheckLastSavedInstance());
	}

	[Theory]
	[InlineData("TestService_Singleton_Service")]
	[InlineData("TestService_Singleton_Factory")]
	[InlineData("TestService_SingleCall_Service")]
	[InlineData("TestService_SingleCall_Factory")]
	[InlineData("TestService_Scoped_Service")]
	[InlineData("TestService_Scoped_Factory")]
	public void Events_should_work_remotely(string serviceName)
	{
		using var ctx = ValidationSyncContext.Install();

		var serviceEventCallCount = 0;
		var customDelegateEventCallCount = 0;

		using var client = new RemotingClient(
			new ClientConfig()
			{
				ConnectionTimeout = 0,
				SendTimeout = 0,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort,
			});

		client.Connect();

		var proxy = client.CreateProxy<ITestService>(serviceName);

		var serviceEventResetEvent = new ManualResetEventSlim(initialState: false);
		var customDelegateEventResetEvent = new ManualResetEventSlim(initialState: false);

		proxy.ServiceEvent += () =>
		{
			Interlocked.Increment(ref serviceEventCallCount);
			serviceEventResetEvent.Set();
		};

		proxy.CustomDelegateEvent += _ =>
		{
			Interlocked.Increment(ref customDelegateEventCallCount);
			customDelegateEventResetEvent.Set();
		};

		proxy.FireServiceEvent();
		proxy.FireCustomDelegateEvent();

		serviceEventResetEvent.Wait(1000);
		customDelegateEventResetEvent.Wait(1000);

		Assert.Equal(1, serviceEventCallCount);
		Assert.Equal(1, customDelegateEventCallCount);
		CheckServerErrorCount();
	}

	[Fact]
	public void External_types_should_work_as_remote_service_parameters()
	{
		// Reset server error state to ensure this test only verifies errors produced within its own scope.
		// Other tests in the shared collection may increment ServerErrorCount on the shared ServerFixture instance.
		_serverFixture.ServerErrorCount = 0;
		_serverFixture.LastServerError = null;

		DataClass parameterValue = null;

		_serverFixture.TestService.TestExternalTypeParameterFake = arg =>
		{
			_remoteServiceCalled = true;
			parameterValue = arg;
		};

		void ClientAction()
		{
			using var ctx = ValidationSyncContext.Install();

			try
			{
				using var client = new RemotingClient(new ClientConfig()
				{
					ConnectionTimeout = 0,
					Channel = ClientChannel,
					MessageEncryption = false,
					ServerPort = _serverFixture.Server.Config.NetworkPort,
				});

				client.Connect();

				var proxy = client.CreateProxy<ITestService>();
				proxy.TestExternalTypeParameter(new DataClass() { Value = 42 });

				Assert.Equal(42, parameterValue.Value);
			}
			catch (Exception e)
			{
				_testOutputHelper.WriteLine(e.ToString());
				throw;
			}
		}

		var clientThread = new Thread(ClientAction);
		clientThread.Start();
		clientThread.Join();

		Assert.True(_remoteServiceCalled);
		CheckServerErrorCount();
	}

	[Fact]
	public void Generic_methods_should_be_called_correctly()
	{
		using var ctx = ValidationSyncContext.Install();

		using var client = new RemotingClient(new ClientConfig()
		{
			ConnectionTimeout = 0,
			Channel = ClientChannel,
			MessageEncryption = false,
			ServerPort = _serverFixture.Server.Config.NetworkPort,
		});

		client.Connect();
		var proxy = client.CreateProxy<IGenericEchoService>();

		var result = proxy.Echo("Yay");

		Assert.Equal("Yay", result);
		CheckServerErrorCount();
	}

	[Fact]
	public void Inherited_methods_should_be_called_correctly()
	{
		using var ctx = ValidationSyncContext.Install();

		using var client = new RemotingClient(new ClientConfig()
		{
			ConnectionTimeout = 0,
			Channel = ClientChannel,
			MessageEncryption = false,
			ServerPort = _serverFixture.Server.Config.NetworkPort,
		});

		client.Connect();
		var proxy = client.CreateProxy<ITestService>();

		var result = proxy.BaseMethod();

		Assert.True(result);
		CheckServerErrorCount();
	}

	[Fact]
	public void Enum_arguments_should_be_passed_correctly()
	{
		using var ctx = ValidationSyncContext.Install();

		using var client = new RemotingClient(new ClientConfig()
		{
			ConnectionTimeout = 0,
			Channel = ClientChannel,
			MessageEncryption = false,
			ServerPort = _serverFixture.Server.Config.NetworkPort
		});

		client.Connect();
		var proxy = client.CreateProxy<IEnumTestService>();

		var resultFirst = proxy.Echo(TestEnum.First);
		var resultSecond = proxy.Echo(TestEnum.Second);

		Assert.Equal(TestEnum.First, resultFirst);
		Assert.Equal(TestEnum.Second, resultSecond);
		CheckServerErrorCount();
	}

	[Fact]
	public void Missing_method_throws_RemoteInvocationException()
	{
		using var ctx = ValidationSyncContext.Install();

		using var client = new RemotingClient(new ClientConfig()
		{
			ConnectionTimeout = 0,
			InvocationTimeout = 0,
			SendTimeout = 0,
			Channel = ClientChannel,
			MessageEncryption = false,
			ServerPort = _serverFixture.Server.Config.NetworkPort
		});

		// simulate MissingMethodException
		var mb = new CustomMessageBuilder
		{
			ProcessMethodCallMessage = m =>
			{
				if (m.MethodName == "TestMethod")
				{
					m.MethodName = "Missing Method";
				}
			}
		};

		client.MethodCallMessageBuilder = mb;
		client.Connect();

		var proxy = client.CreateProxy<ITestService>();
		var ex = Assert.Throws<RemoteInvocationException>(() => proxy.TestMethod(null));

		// a localized message similar to "Method 'Missing method' not found"
		Assert.NotNull(ex);
		Assert.Contains("Missing Method", ex.Message);

		Console.WriteLine(_serverFixture.LastServerError);
		CheckServerErrorCount();
	}

	[Fact]
	public void Missing_service_throws_RemoteInvocationException()
	{
		using var ctx = ValidationSyncContext.Install();

		using var client = new RemotingClient(new ClientConfig()
		{
			ConnectionTimeout = 0,
			InvocationTimeout = 0,
			SendTimeout = 0,
			Channel = ClientChannel,
			MessageEncryption = false,
			ServerPort = _serverFixture.Server.Config.NetworkPort
		});

		client.Connect();

		var proxy = client.CreateProxy<IDisposable>();
		var ex = Assert.Throws<RemoteInvocationException>(proxy.Dispose);

		// a localized message similar to "Service 'System.IDisposable' is not registered"
		Assert.NotNull(ex);
		Assert.Contains("IDisposable", ex.Message);
		CheckServerErrorCount();
	}

	[Fact]
	public void Error_method_throws_Exception()
	{
		using var ctx = ValidationSyncContext.Install();

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 5,
				InvocationTimeout = 5,
				SendTimeout = 5,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort
			});

			client.Connect();

			var proxy = client.CreateProxy<ITestService>();
			var ex = Assert.Throws<RemoteInvocationException>(() =>
					proxy.Error(nameof(Error_method_throws_Exception)))
				.GetInnermostException();

			Assert.NotNull(ex);
			Assert.Equal(nameof(Error_method_throws_Exception), ex.Message);
		}
		finally
		{
			// reset the error counter for other tests
			_serverFixture.ServerErrorCount = 0;
		}
	}

	[Fact]
	[SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
	public async Task ErrorAsync_method_throws_Exception()
	{
		// using var ctx = ValidationSyncContext.Install(); // fails?

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 5,
				InvocationTimeout = 5,
				SendTimeout = 5,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort
			});

			client.Connect();

			var proxy = client.CreateProxy<ITestService>();
			var ex = (await Assert.ThrowsAsync<RemoteInvocationException>(async () =>
				{
					await proxy.ErrorAsync(nameof(ErrorAsync_method_throws_Exception)).ConfigureAwait(false);
				})
				.ConfigureAwait(false)).GetInnermostException();

			Assert.NotNull(ex);
			Assert.Equal(nameof(ErrorAsync_method_throws_Exception), ex.Message);
		}
		finally
		{
			// reset the error counter for other tests
			_serverFixture.ServerErrorCount = 0;
		}
	}

	[Fact]
	public void NonSerializableError_method_throws_Exception()
	{
		using var ctx = ValidationSyncContext.Install();

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 5,
				InvocationTimeout = 5,
				SendTimeout = 5,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort
			});

			client.Connect();

			var proxy = client.CreateProxy<ITestService>();

			var ex = Assert.Throws<RemoteInvocationException>(() =>
					proxy.NonSerializableError("Hello", "Serializable", "World"))
						.GetInnermostException();

			Assert.NotNull(ex);
			Assert.IsType<SerializableException>(ex);

			if (ex is SerializableException sx)
			{
				Assert.Equal("NonSerializable", sx.SourceTypeName);
				Assert.Equal("Hello", ex.Message);
				
				// Extract values from Data dictionary, handling JObject-wrapped values from BSON serialization
				string ExtractDataValue(object value) =>
					value is Newtonsoft.Json.Linq.JObject jObj ? jObj["V"]?.ToString() : value?.ToString();
				
				Assert.Equal("Serializable", ExtractDataValue(ex.Data["Serializable"]));
				Assert.Equal("World", ExtractDataValue(ex.Data["World"]));
				Assert.NotNull(ex.StackTrace);
			}
		}
		finally
		{
			// reset the error counter for other tests
			_serverFixture.ServerErrorCount = 0;
		}
	}

	[Fact]
	public void Nonserializable_method_return_value_throws_Exception()
	{
		using var ctx = ValidationSyncContext.Install();

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 5,
				InvocationTimeout = 5,
				SendTimeout = 5,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort
			});

			client.Connect();

			var proxy = client.CreateProxy<ITestService>();
			var ex = Assert.Throws<RemoteInvocationException>(() =>
				proxy.NonSerializableReturnValue("Hello"));

			Assert.NotNull(ex);
			Assert.Contains("Failed to serialize method return value", ex.Message);
			Assert.NotNull(ex.InnerException);
		}
		finally
		{
			// reset the error counter for other tests
			_serverFixture.ServerErrorCount = 0;
		}
	}

	[Fact]
	public void AfterCall_event_handler_can_translate_exceptions_to_improve_diagnostics()
	{
		// replace cryptic database error report with a user-friendly error message
		void AfterCall(object sender, ServerRpcContext ctx)
		{
			var errorMsg = ctx.Exception?.InnerException?.Message ?? ctx.Exception?.Message ?? string.Empty;
			if (errorMsg.StartsWith("23503:"))
				ctx.Exception = new Exception("Deleting clients is not allowed.",
					ctx.Exception);
		}

		using var ctx = ValidationSyncContext.Install();
		_serverFixture.Server.AfterCall += AfterCall;

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 5,
				InvocationTimeout = 5,
				SendTimeout = 5,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort
			});

			client.Connect();

			var dbError = "23503: delete from table 'clients' violates " +
			              "foreign key constraint 'order_client_fk' on table 'orders'";

			// simulate a database error on the server-side
			var proxy = client.CreateProxy<ITestService>();
			var ex = Assert.Throws<Exception>(() => proxy.Error(dbError));

			Assert.NotNull(ex);
			Assert.Equal("Deleting clients is not allowed.", ex.Message);
			Assert.NotNull(ex.InnerException);
			Assert.IsType<RemoteInvocationException>(ex.InnerException);
			Assert.Equal(dbError, ex.InnerException.InnerException.Message);
		}
		finally
		{
			// reset the error counter for other tests
			_serverFixture.ServerErrorCount = 0;
			_serverFixture.Server.AfterCall -= AfterCall;
		}
	}

	[Fact]
	public void Failing_component_constructor_throws_RemoteInvocationException()
	{
		using var ctx = ValidationSyncContext.Install();

		using var client = new RemotingClient(new ClientConfig()
		{
			ConnectionTimeout = 3,
			InvocationTimeout = 3,
			SendTimeout = 3,
			Channel = ClientChannel,
			MessageEncryption = false,
			ServerPort = _serverFixture.Server.Config.NetworkPort,
		});

		client.Connect();

		var proxy = client.CreateProxy<IFailingService>();
		var ex = Assert.Throws<RemoteInvocationException>(() => proxy.Hello());

		Assert.NotNull(ex);
		Assert.Contains("FailingService", ex.Message);
	}

	[Fact]
	[SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "<Pending>")]
	public async Task Disposed_client_subscription_doesnt_break_other_clients()
	{
		using var ctx = ValidationSyncContext.Install();

		async Task Roundtrip(bool encryption)
		{
			var oldEncryption = _serverFixture.Server.Config.MessageEncryption;
			_serverFixture.Server.Config.MessageEncryption = encryption;

			try
			{
				RemotingClient CreateClient() => new RemotingClient(new ClientConfig()
				{
					Channel = ClientChannel,
					ServerPort = _serverFixture.Server.Config.NetworkPort,
					MessageEncryption = encryption,
					ConnectionTimeout = encryption ? 0 : 5,
					KeySize = 1024
				});

				using var client1 = CreateClient();
				using var client2 = CreateClient();

				client1.Connect();
				client2.Connect();

				var proxy1 = client1.CreateProxy<ITestService>();
				var fired1 = new TaskCompletionSource<bool>();
				proxy1.ServiceEvent += () => fired1.TrySetResult(true);

				var proxy2 = client2.CreateProxy<ITestService>();
				var fired2 = new TaskCompletionSource<bool>();
				proxy2.ServiceEvent += () => fired2.TrySetResult(true);

				// early disposal, proxy1 subscription isn't canceled
				client1.Disconnect();

				proxy2.FireServiceEvent();
				Assert.True(await fired2.Task.ConfigureAwait(false));
				Assert.True(fired2.Task.IsCompleted);
				Assert.False(fired1.Task.IsCompleted);
			}
			finally
			{
				_serverFixture.Server.Config.MessageEncryption = oldEncryption;

				// reset the error counter for other tests
				_serverFixture.ServerErrorCount = 0;
			}
		}

		// works!
		await Roundtrip(encryption: false).ConfigureAwait(false);

		// fails!
		await Roundtrip(encryption: true).ConfigureAwait(false);
	}

	[Fact]
	public void DataTable_roundtrip_works_issue60()
	{
		using var ctx = ValidationSyncContext.Install();

		using var client = new RemotingClient(new ClientConfig()
		{
			ConnectionTimeout = 0,
			InvocationTimeout = 0,
			SendTimeout = 0,
			Channel = ClientChannel,
			MessageEncryption = false,
			ServerPort = _serverFixture.Server.Config.NetworkPort,
		});

		client.Connect();
		var proxy = client.CreateProxy<ITestService>();

		var dt = new DataTable();
		dt.TableName = "Issue60";
		dt.Columns.Add("CODE");
		dt.Rows.Add(dt.NewRow());
		dt.AcceptChanges();

		var dt2 = proxy.TestDt(dt, 1);
		Assert.NotNull(dt2);
	}

	[Fact]
	[SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "<Pending>")]
	public void Large_messages_are_sent_and_received()
	{
		// max payload size, in bytes
		var maxSize = 2 * 1024 * 1024 + 1;

		using var ctx = ValidationSyncContext.Install();

		using var client = new RemotingClient(new ClientConfig()
		{
			ConnectionTimeout = 0,
			InvocationTimeout = 0,
			SendTimeout = 0,
			Channel = ClientChannel,
			MessageEncryption = false,
			ServerPort = _serverFixture.Server.Config.NetworkPort,
		});

		client.Connect();
		var proxy = client.CreateProxy<ITestService>();

		// shouldn't throw exceptions
		Roundtrip("Payload", maxSize);
		Roundtrip(new byte[] { 1, 2, 3, 4, 5 }, maxSize);
		Roundtrip(new int[] { 12345, 67890 }, maxSize);

		void Roundtrip<T>(T payload, int maxSize) where T : class
		{
			var lastSize = 0;
			try
			{
				while (true)
				{
					// a -> aa -> aaaa ...
					var (dup, size) = proxy.Duplicate(payload);
					if (size >= maxSize)
						break;

					// save the size for error reporting
					lastSize = size;
					payload = dup;
				}
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException($"Failed to handle " +
				                                    $"payload larger than {lastSize}: {ex.Message}", ex);
			}
		}
	}

	[Fact]
	[SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "Not applicable")]
	public async Task BeforeCall_and_AfterCall_events_are_triggered_on_success()
	{
		var beforeCallFired = new AsyncCounter();

		void BeforeCall(object sender, ServerRpcContext e) =>
			beforeCallFired++;

		var afterCallFired = new AsyncCounter();

		void AfterCall(object sender, ServerRpcContext e) =>
			afterCallFired++;

		using var ctx = ValidationSyncContext.Install();
		_serverFixture.Server.BeforeCall += BeforeCall;
		_serverFixture.Server.AfterCall += AfterCall;

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 0,
				InvocationTimeout = 0,
				SendTimeout = 0,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort,
			});

			client.Connect();

			// test one-way method
			var proxy = client.CreateProxy<ITestService>();
			proxy.OneWayMethod();

			// test normal method
			Assert.Equal("Hello", proxy.Echo("Hello"));

			await beforeCallFired[2].Timeout(1).ConfigureAwait(false);
			await afterCallFired[2].Timeout(1).ConfigureAwait(false);
		}
		finally
		{
			_serverFixture.Server.AfterCall -= AfterCall;
			_serverFixture.Server.BeforeCall -= BeforeCall;
		}
	}

	[Fact]
	[SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "Not applicable")]
	public async Task BeforeCall_and_AfterCall_events_are_triggered_on_failures()
	{
		var beforeCallFired = new AsyncCounter();

		void BeforeCall(object sender, ServerRpcContext e) =>
			beforeCallFired++;

		var afterCallFired = new AsyncCounter();

		void AfterCall(object sender, ServerRpcContext e) =>
			afterCallFired++;

		using var ctx = ValidationSyncContext.Install();
		_serverFixture.Server.BeforeCall += BeforeCall;
		_serverFixture.Server.AfterCall += AfterCall;

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 0,
				InvocationTimeout = 0,
				SendTimeout = 0,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort,
			});

			client.Connect();

			// test failing method
			var proxy = client.CreateProxy<ITestService>();
			Assert.Throws<RemoteInvocationException>(() => proxy.Error("Bang"));

			await beforeCallFired[1].Timeout(1).ConfigureAwait(false);
			await afterCallFired[1].Timeout(1).ConfigureAwait(false);
		}
		finally
		{
			_serverFixture.Server.AfterCall -= AfterCall;
			_serverFixture.Server.BeforeCall -= BeforeCall;
		}
	}

	[Fact]
	public void BeginCall_event_handler_can_intercept_and_cancel_method_calls()
	{
		var counter = 0;

		void InterceptMethodCalls(object sender, ServerRpcContext e)
		{
			Interlocked.Increment(ref counter);

			// swap Echo and Reverse methods
			e.MethodCallMessage.MethodName = e.MethodCallMessage.MethodName switch
			{
				"Echo" => "Reverse",
				"Reverse" => "Echo",
				var others => others
			};

			// disable IHobbitService
			if (e.MethodCallMessage.ServiceName.Contains("IHobbitService"))
			{
				e.Cancel = true;
			}
		}

		using var ctx = ValidationSyncContext.Install();
		_serverFixture.Server.BeginCall += InterceptMethodCalls;

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 0,
				InvocationTimeout = 0,
				SendTimeout = 0,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort,
			});

			client.Connect();

			// try swapped methods
			var proxy = client.CreateProxy<ITestService>();
			Assert.Equal("321", proxy.Echo("123"));
			Assert.Equal("Hello", proxy.Reverse("Hello"));

			// try disabled service
			var hobbit = client.CreateProxy<IHobbitService>();
			Assert.Throws<RemoteInvocationException>(() =>
				hobbit.QueryHobbits(h => h.LastName != ""));

			// check interception counter
			Assert.Equal(3, counter);
		}
		finally
		{
			_serverFixture.Server.BeginCall -= InterceptMethodCalls;
		}
	}

	[Fact]
	public void Authentication_is_taken_into_account_and_RejectCall_event_is_fired()
	{
		var rejectedMethod = string.Empty;

		void RejectCall(object sender, ServerRpcContext e) =>
			rejectedMethod = e.MethodCallMessage.MethodName;

		var server = _serverFixture.Server;
		server.RejectCall += RejectCall;
		server.Config.AuthenticationRequired = true;

		using var ctx = ValidationSyncContext.Install();

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 0,
				InvocationTimeout = 0,
				SendTimeout = 0,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort,
			});

			client.Connect();

			var proxy = client.CreateProxy<IFailingService>();
			var ex = Assert.Throws<RemoteInvocationException>(proxy.Hello);

			// Session is not authenticated
			Assert.Contains("authenticated", ex.Message);

			// Method call was rejected
			Assert.Equal("Hello", rejectedMethod);
		}
		finally
		{
			server.Config.AuthenticationRequired = false;
			server.RejectCall -= RejectCall;
		}
	}

	[Fact]
	public void Authentication_handler_has_access_to_the_current_session()
	{
		var server = _serverFixture.Server;
		var authProvider = server.Config.AuthenticationProvider;
		server.Config.AuthenticationRequired = true;
		server.Config.AuthenticationProvider = new FakeAuthProvider
		{
			AuthenticateFake = c => RemotingSession.Current != null
		};

		using var ctx = ValidationSyncContext.Install();

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 0,
				InvocationTimeout = 0,
				SendTimeout = 0,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort,
				Credentials = [new()],
			});

			client.Connect();

			var proxy = client.CreateProxy<ITestService>();
			Assert.Equal("123", proxy.Reverse("321"));
		}
		finally
		{
			server.Config.AuthenticationProvider = authProvider;
			server.Config.AuthenticationRequired = false;
		}
	}

	[Fact]
	public void Broken_auhentication_handler_doesnt_break_the_server()
	{
		var server = _serverFixture.Server;
		var authProvider = server.Config.AuthenticationProvider;
		server.Config.AuthenticationRequired = true;
		server.Config.AuthenticationProvider = new FakeAuthProvider
		{
			AuthenticateFake = c => throw new Exception("Broken")
		};

		using var ctx = ValidationSyncContext.Install();

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 3,
				InvocationTimeout = 3,
				SendTimeout = 3,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort,
				Credentials = [new()],
			});

			var ex = Assert.Throws<SecurityException>(client.Connect);

			Assert.Contains("auth", ex.Message.ToLower());
			Assert.Contains("failed", ex.Message);
		}
		finally
		{
			server.Config.AuthenticationProvider = authProvider;
			server.Config.AuthenticationRequired = false;
			_serverFixture.ServerErrorCount = 0;
		}
	}

	[Fact]
	public void Authentication_handler_can_check_client_address()
	{
		var server = _serverFixture.Server;
		var authProvider = server.Config.AuthenticationProvider;
		server.Config.AuthenticationRequired = true;
		server.Config.AuthenticationProvider = new FakeAuthProvider
		{
			AuthenticateFake = c =>
			{
				var address = RemotingSession.Current?.ClientAddress ??
				              throw new ArgumentNullException("ClientAddress");

				// allow only localhost connections
				return address.Contains("127.0.0.1") || // ipv4
				       address.Contains("[::1]"); // ipv6
			}
		};

		using var ctx = ValidationSyncContext.Install();

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 0,
				InvocationTimeout = 0,
				SendTimeout = 0,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort,
				Credentials = [new Credential()],
			});

			client.Connect();

			var proxy = client.CreateProxy<ITestService>();
			Assert.Equal("123", proxy.Reverse("321"));
		}
		finally
		{
			server.Config.AuthenticationProvider = authProvider;
			server.Config.AuthenticationRequired = false;
		}
	}

	[Fact]
	public void Authentication_can_fail_then_succeed()
	{
		var server = _serverFixture.Server;
		var authProvider = server.Config.AuthenticationProvider;
		server.Config.AuthenticationRequired = true;
		server.Config.AuthenticationProvider = new FakeAuthProvider
		{
			AuthenticateFake = c => c.Length == 2
		};

		using var ctx = ValidationSyncContext.Install();

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 0,
				InvocationTimeout = 0,
				SendTimeout = 0,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort,
				Credentials = [new()],
			});

			Assert.Throws<SecurityException>(client.Connect);

			client.Config.Credentials = [new(), new()];
			client.Connect();

			var proxy = client.CreateProxy<ITestService>();
			Assert.Equal("123", proxy.Reverse("321"));
		}
		finally
		{
			server.Config.AuthenticationProvider = authProvider;
			server.Config.AuthenticationRequired = false;
			_serverFixture.ServerErrorCount = 0;
		}
	}

	[Fact]
	public void RemotingClient_can_disconnect_and_connect_again()
	{
		using var ctx = ValidationSyncContext.Install();

		using var client = new RemotingClient(new ClientConfig()
		{
			ConnectionTimeout = 0,
			InvocationTimeout = 0,
			SendTimeout = 0,
			MessageEncryption = false,
			Channel = ClientChannel,
			ServerPort = _serverFixture.Server.Config.NetworkPort,
		});

		client.Connect();

		var proxy = client.CreateProxy<ISessionAwareService>();
		Assert.NotNull(proxy.ClientAddress);

		client.Disconnect();

		client.Connect();
		proxy = client.CreateProxy<ISessionAwareService>();
		Assert.NotNull(proxy.ClientAddress);
	}

	[Fact]
	public void ServerComponent_can_track_client_network_address()
	{
		using var ctx = ValidationSyncContext.Install();

		using var client = new RemotingClient(new ClientConfig()
		{
			ConnectionTimeout = 0,
			InvocationTimeout = 0,
			SendTimeout = 0,
			MessageEncryption = false,
			Channel = ClientChannel,
			ServerPort = _serverFixture.Server.Config.NetworkPort,
		});

		client.Connect();

		var proxy = client.CreateProxy<ISessionAwareService>();

		// what's my address as seen by remote server?
		Assert.NotNull(proxy.ClientAddress);
	}

	[Fact]
	public void Scoped_service_is_resolved_within_the_remote_call_scope()
	{
		using var ctx = ValidationSyncContext.Install();

		using var client = new RemotingClient(new ClientConfig()
		{
			ConnectionTimeout = 0,
			InvocationTimeout = 0,
			SendTimeout = 0,
			MessageEncryption = false,
			Channel = ClientChannel,
			ServerPort = _serverFixture.Server.Config.NetworkPort,
		});

		client.Connect();

		var proxy = client.CreateProxy<IServiceWithDeps>();
		var guid = proxy.ScopedServiceInstanceId;

		// empty Guid means that ServiceWithDeps has got different ScopedService instances
		// but it should get the same scoped service instances across the whore resolution graph
		Assert.NotEqual(Guid.Empty, guid);

		// every remote call should produce a new ScopedService instance
		Assert.NotEqual(guid, proxy.ScopedServiceInstanceId);
		Assert.NotEqual(proxy.ScopedServiceInstanceId, proxy.ScopedServiceInstanceId);
	}

	[Fact]
	[SuppressMessage("Usage", "xUnit1030:Do not call ConfigureAwait in test method", Justification = "Not applicable")]
	public async Task Logon_and_logoff_events_are_triggered()
	{
		using var ctx = ValidationSyncContext.Install();

		void CheckSession(string operation)
		{
			var rs = RemotingSession.Current;
			Assert.NotNull(rs);
			Assert.True(rs?.IsAuthenticated);
			Assert.NotNull(rs?.ClientAddress);
			Assert.NotNull(rs?.Identity);
			Console.WriteLine($"Client {rs.Identity.Name} from {rs.ClientAddress} is {operation}");
		}

		var logon = new AsyncCounter();

		void Logon(object sender, EventArgs _)
		{
			logon++;
			CheckSession("logged on");
		}

		var logoff = new AsyncCounter();

		void Logoff(object sender, EventArgs _)
		{
			logoff++;
			CheckSession("logged off");
		}

		var server = _serverFixture.Server;
		var authProvider = server.Config.AuthenticationProvider;
		server.Config.AuthenticationProvider = new FakeAuthProvider();

		server.Logon += Logon;
		server.Logoff += Logoff;
		server.Config.AuthenticationRequired = true;

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 0,
				InvocationTimeout = 0,
				SendTimeout = 0,
				Channel = ClientChannel,
				MessageEncryption = false,
				Credentials = [new()],
				ServerPort = _serverFixture.Server.Config.NetworkPort,
			});

			client.Connect();

			var proxy = client.CreateProxy<ITestService>();
			Assert.Equal("Hello", proxy.Echo("Hello"));

			client.Disconnect();

			await logon[1].Timeout(1).ConfigureAwait(false);
			await logoff[1].Timeout(1).ConfigureAwait(false);
		}
		finally
		{
			server.Config.AuthenticationProvider = authProvider;
			server.Config.AuthenticationRequired = false;
			server.Logoff -= Logoff;
			server.Logon -= Logon;
		}
	}

	[Fact]
	public void BeginCall_event_handler_can_bypass_authentication_for_chosen_method()
	{
		void BypassAuthorizationForEcho(object sender, ServerRpcContext e) =>
			e.AuthenticationRequired =
				e.MethodCallMessage.MethodName != "Echo";

		using var ctx = ValidationSyncContext.Install();
		_serverFixture.Server.Config.AuthenticationRequired = true;
		_serverFixture.Server.BeginCall += BypassAuthorizationForEcho;

		try
		{
			using var client = new RemotingClient(new ClientConfig()
			{
				ConnectionTimeout = 0,
				InvocationTimeout = 0,
				SendTimeout = 0,
				Channel = ClientChannel,
				MessageEncryption = false,
				ServerPort = _serverFixture.Server.Config.NetworkPort,
			});

			client.Connect();

			// try allowed method "Echo"
			var proxy = client.CreateProxy<ITestService>();
			Assert.Equal("This method is allowed", proxy.Echo("This method is allowed"));

			// try disallowed method "Reverse"
			var ex = Assert.Throws<RemoteInvocationException>(() => proxy.Reverse("This method is not allowed"));
			Assert.Contains("auth", ex.Message);
		}
		finally
		{
			_serverFixture.Server.BeginCall -= BypassAuthorizationForEcho;
			_serverFixture.Server.Config.AuthenticationRequired = false;
		}
	}

	[Fact]
	public void CreateProxy_methods_should_produce_equivalent_results()
	{
		using var ctx = ValidationSyncContext.Install();

		using var client = new RemotingClient(new ClientConfig()
		{
			ConnectionTimeout = 0,
			Channel = ClientChannel,
			MessageEncryption = false,
			ServerPort = _serverFixture.Server.Config.NetworkPort,
		});

		client.Connect();

		var proxy1 = client.CreateProxy<IGenericEchoService>();
		var result1 = proxy1.Echo("Yay");
		Assert.Equal("Yay", result1);

		var proxy2 = client.CreateProxy(typeof(IGenericEchoService)) as IGenericEchoService;
		var result2 = proxy2.Echo("Yay");
		Assert.Equal("Yay", result2);

		var svcref = new ServiceReference(typeof(IGenericEchoService).AssemblyQualifiedName, "");
		var proxy3 = client.CreateProxy(svcref) as IGenericEchoService;
		var result3 = proxy3.Echo("Yay");
		Assert.Equal("Yay", result3);

		CheckServerErrorCount();
	}

	[Fact]
	public void Custom_proxy_builder_can_be_used_for_interception()
	{
		using var ctx = ValidationSyncContext.Install();

		using var client = new RemotingClient(new ClientConfig()
		{
			ConnectionTimeout = 0,
			Channel = ClientChannel,
			MessageEncryption = false,
			ServerPort = _serverFixture.Server.Config.NetworkPort,
			ProxyBuilder = new CustomProxyBuilder(),
		});

		client.Connect();

		var proxy1 = client.CreateProxy<IGenericEchoService>();
		Assert.Equal("[Yay]", proxy1.Echo("Yay"));
		Assert.Equal(1, proxy1.Echo(1));

		var proxy2 = client.CreateProxy(typeof(IGenericEchoService)) as IGenericEchoService;
		Assert.Equal("[Yay]", proxy2.Echo("Yay"));
		Assert.Equal(1, proxy2.Echo(1));

		var svcref = new ServiceReference(typeof(IGenericEchoService).AssemblyQualifiedName, "");
		var proxy3 = client.CreateProxy(svcref) as IGenericEchoService;
		Assert.Equal("[Yay]", proxy3.Echo("Yay"));
		Assert.Equal(1, proxy3.Echo(1));

		CheckServerErrorCount();
	}

	[Fact]
	public void NonDeserializableObject_TriggersException()
	{
		using var ctx = ValidationSyncContext.Install();

		using var client = new RemotingClient(new ClientConfig()
		{
			ConnectionTimeout = 0,
			InvocationTimeout = 0,
			SendTimeout = 0,
			MessageEncryption = false,
			Channel = ClientChannel,
			ServerPort = _serverFixture.Server.Config.NetworkPort,
		});

		client.Connect();

		var proxy = client.CreateProxy<ITestService>();
		try
		{
			var ex = Assert.Throws<RemoteInvocationException>(() =>
				proxy.Duplicate(new NonDeserializable(123)));

			Assert.Contains(NonDeserializable.ErrorMessage, ex.Message);
		}
		finally
		{
			_serverFixture.ServerErrorCount = 0;
		}
	}
}