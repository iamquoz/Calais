using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Calais.Configuration;
using Calais.Exceptions;
using Calais.Models;

namespace Calais.Core;

internal sealed class CustomMethodInvoker
{
	private static readonly ConditionalWeakTable<Type, ILookup<string, MethodInfo>> MethodsByName =
		new();
	private readonly CalaisOptions _options;
	private readonly IServiceProvider _services;
	private readonly IReadOnlyList<ICalaisCustomFilterMethods> _filterMethods;
	private readonly IReadOnlyList<ICalaisCustomSortMethods> _sortMethods;

	public CustomMethodInvoker(
		CalaisOptions options,
		IServiceProvider services,
		IEnumerable<ICalaisCustomFilterMethods>? filterMethods,
		IEnumerable<ICalaisCustomSortMethods>? sortMethods
	)
	{
		_options = options;
		_services = services;
		_filterMethods = filterMethods?.ToList() ?? [];
		_sortMethods = sortMethods?.ToList() ?? [];
	}

	public bool TryApplyFilter<TEntity>(
		IQueryable<TEntity> source,
		FilterDescriptor descriptor,
		out IQueryable<TEntity> result
	)
		where TEntity : class
	{
		result = source;
		if (string.IsNullOrWhiteSpace(descriptor.Field))
			return false;

		var candidates = FindCandidates(_filterMethods, descriptor.Field!);
		if (candidates.Count == 0)
			return false;

		var compatible = candidates
			.Select(candidate => CloseAndValidate<TEntity>(candidate, typeof(CalaisFilterContext)))
			.Where(candidate => candidate.Method != null)
			.ToList();

		if (compatible.Count != 1)
			return HandleInvalidCustomMethod(
				descriptor.Field!,
				"filter",
				compatible.Count,
				candidates.Count
			);

		var context = new CalaisFilterContext(descriptor, _services);
		result =
			(IQueryable<TEntity>)
				compatible[0].Method!.Invoke(compatible[0].Target, [source, context])!;
		return true;
	}

	public bool TryApplySort<TEntity>(
		IQueryable<TEntity> source,
		SortDescriptor descriptor,
		bool useThenBy,
		out IQueryable<TEntity> result
	)
		where TEntity : class
	{
		result = source;
		if (string.IsNullOrWhiteSpace(descriptor.Field))
			return false;

		var candidates = FindCandidates(_sortMethods, descriptor.Field);
		if (candidates.Count == 0)
			return false;

		var compatible = candidates
			.Select(candidate => CloseAndValidate<TEntity>(candidate, typeof(CalaisSortContext)))
			.Where(candidate => candidate.Method != null)
			.ToList();

		if (compatible.Count != 1)
			return HandleInvalidCustomMethod(
				descriptor.Field,
				"sort",
				compatible.Count,
				candidates.Count
			);

		var context = new CalaisSortContext(descriptor, useThenBy, _services);
		result =
			(IQueryable<TEntity>)
				compatible[0].Method!.Invoke(compatible[0].Target, [source, context])!;
		return true;
	}

	public bool HasCustomFilter<TEntity>(FilterDescriptor descriptor, out bool invalid)
		where TEntity : class
	{
		invalid = false;
		if (string.IsNullOrWhiteSpace(descriptor.Field))
			return false;

		var candidates = FindCandidates(_filterMethods, descriptor.Field!);
		if (candidates.Count == 0)
			return false;

		var compatibleCount = candidates
			.Select(candidate => CloseAndValidate<TEntity>(candidate, typeof(CalaisFilterContext)))
			.Count(candidate => candidate.Method != null);

		invalid = compatibleCount != 1;
		return true;
	}

	private bool HandleInvalidCustomMethod(
		string field,
		string kind,
		int compatibleCount,
		int candidateCount
	)
	{
		if (!_options.ThrowOnInvalidFields)
			return false;

		var problem = compatibleCount > 1 ? "ambiguous" : "incompatible";
		throw new ExpressionBuildException(
			$"Custom {kind} method '{field}' is {problem}. Found {candidateCount} candidate method(s)."
		);
	}

	private static IReadOnlyList<CustomMethodCandidate> FindCandidates<TMethods>(
		IReadOnlyList<TMethods> methodServices,
		string field
	)
	{
		List<CustomMethodCandidate>? candidates = null;

		foreach (var methodService in methodServices)
		{
			if (methodService == null)
				continue;

			var methods = MethodsByName.GetValue(methodService.GetType(), static type =>
				type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
					.ToLookup(method => method.Name, StringComparer.OrdinalIgnoreCase)
			);
			foreach (var method in methods[field])
				(candidates ??= []).Add(new CustomMethodCandidate(methodService, method));
		}

		return candidates is null ? [] : candidates;
	}

	private static CustomMethodCandidate CloseAndValidate<TEntity>(
		CustomMethodCandidate candidate,
		Type contextType
	)
		where TEntity : class
	{
		if (candidate.Method == null)
			return candidate;

		var method = TryCloseGenericMethod<TEntity>(candidate.Method);
		if (method == null)
			return candidate with { Method = null };

		var parameters = method.GetParameters();
		if (parameters.Length != 2)
			return candidate with { Method = null };

		if (!parameters[0].ParameterType.IsAssignableFrom(typeof(IQueryable<TEntity>)))
			return candidate with { Method = null };

		if (parameters[1].ParameterType != contextType)
			return candidate with { Method = null };

		if (!typeof(IQueryable<TEntity>).IsAssignableFrom(method.ReturnType))
			return candidate with { Method = null };

		return candidate with
		{
			Method = method,
		};
	}

	private static MethodInfo? TryCloseGenericMethod<TEntity>(MethodInfo method)
		where TEntity : class
	{
		if (!method.IsGenericMethodDefinition)
			return method.ContainsGenericParameters ? null : method;

		var arguments = method.GetGenericArguments();
		if (arguments.Length != 1)
			return null;

		try
		{
			return method.MakeGenericMethod(typeof(TEntity));
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	private sealed record CustomMethodCandidate(object Target, MethodInfo? Method);
}
