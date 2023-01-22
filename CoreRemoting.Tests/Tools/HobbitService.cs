using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace CoreRemoting.Tests.Tools;

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