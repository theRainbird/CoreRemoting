using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using CoreRemoting.DependencyInjection;
using Xunit;

namespace CoreRemoting.Tests
{
    public class LinqExpressionTests
    {
        #region Service with method using expressions

        public class Hobbit
        {
            public string FirstName { get; set; }
            public string LastName { get; set; }
        }

        public interface IHobbitService
        {
            Hobbit QueryHobbits(Expression<Func<Hobbit, bool>> criteriaExpression);
        }

        public class HobbitService : IHobbitService
        {
            private List<Hobbit> _hobbits =
                new List<Hobbit>
                {
                    new Hobbit {FirstName = "Bilbo", LastName = "Baggins"},
                    new Hobbit {FirstName = "Frodo", LastName = "Baggins"},
                    new Hobbit {FirstName = "Peregrin", LastName = "Tuck"}
                };
            
            public Hobbit QueryHobbits(Expression<Func<Hobbit, bool>> criteriaExpression)
            {
                var criteria = criteriaExpression.Compile();

                return _hobbits.FirstOrDefault(criteria);
            }
        }

        #endregion
        
        [Fact]
        public void LinqExpression_should_be_serialized_and_deserialized()
        {
            var serverConfig =
                new ServerConfig()
                {
                    NetworkPort = 9198,
                    RegisterServicesAction = container =>
                        container.RegisterService<IHobbitService, HobbitService>(
                            lifetime: ServiceLifetime.Singleton)
                };

            using var server = new RemotingServer(serverConfig);
            server.Start();

            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0, 
                ServerPort = 9198
            });

            client.Connect();
            var proxy = client.CreateProxy<IHobbitService>();

            var result = proxy.QueryHobbits(h => h.FirstName == "Frodo");
            
            Assert.True(result.FirstName == "Frodo");
        }
    }
}