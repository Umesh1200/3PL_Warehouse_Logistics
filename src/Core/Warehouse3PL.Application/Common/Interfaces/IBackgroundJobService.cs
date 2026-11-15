using System.Linq.Expressions;

namespace Warehouse3PL.Application.Common.Interfaces;

public interface IBackgroundJobService
{
    string Enqueue(Expression<Action> methodCall);
    string Enqueue<T>(Expression<Action<T>> methodCall);
    string Schedule(Expression<Action> methodCall, TimeSpan delay);
    string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay);
    void AddOrUpdateRecurringJob(string recurringJobId, Expression<Action> methodCall, string cronExpression);
    void AddOrUpdateRecurringJob<T>(string recurringJobId, Expression<Action<T>> methodCall, string cronExpression);
}

