using System;
using System.Linq.Expressions;

namespace CoreRemoting.Tests.Tools;

public interface IHobbitService
{
    Hobbit QueryHobbits(Expression<Func<Hobbit, bool>> criteriaExpression);

    Expression<Func<T, bool>> ValidatePredicate<T>(Expression<Func<T, bool>> predicate);
}