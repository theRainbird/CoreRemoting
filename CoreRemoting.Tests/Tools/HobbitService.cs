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

    public Expression<Func<T, bool>> ValidatePredicate<T>(Expression<Func<T, bool>> predicate)
    {
        // add more conditions to the user's predicate, etc.
        return predicate;
    }

    public System.Threading.Tasks.Task<Expression<Func<T, bool>>> ValidatePredicateAsync<T>(Expression<Func<T, bool>> predicate)
    {
        return System.Threading.Tasks.Task.FromResult(predicate);
    }
}