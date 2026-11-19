using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Calais.Core;

internal sealed class CollectionAnyOptimizer : ExpressionVisitor
{
	// One EXISTS for alternatives on the same navigation lets the planner use a semi-join.
	internal static Expression<Func<T, bool>> Combine<T>(Expression<Func<T, bool>> predicate) =>
		(Expression<Func<T, bool>>)new CollectionAnyOptimizer().Visit(predicate);

	protected override Expression VisitBinary(BinaryExpression node)
	{
		var left = Visit(node.Left);
		var right = Visit(node.Right);
		if (
			node.NodeType == ExpressionType.OrElse
			&& left is MethodCallExpression first
			&& right is MethodCallExpression second
			&& first.Method == second.Method
			&& first.Method.DeclaringType == typeof(Enumerable)
			&& first.Method.Name == nameof(Enumerable.Any)
			&& first.Arguments is [MemberExpression a, LambdaExpression p]
			&& second.Arguments is [MemberExpression b, LambdaExpression q]
			&& a.Member == b.Member
			&& a.Expression == b.Expression
		)
		{
			var alternative = ReplacingExpressionVisitor.Replace(
				q.Parameters[0],
				p.Parameters[0],
				q.Body
			);
			var combined = Expression.Lambda(Expression.OrElse(p.Body, alternative), p.Parameters);
			return Expression.Call(first.Method, a, combined);
		}

		return node.Update(left, node.Conversion, right);
	}
}
