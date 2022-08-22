﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace DynamicExpresso
{
	/// <summary>
	/// Represents a lambda expression that can be invoked. This class is thread safe.
	/// </summary>
	public class Lambda
	{
		private readonly Expression _expression;
		private readonly ParserArguments _parserArguments;
		private readonly Lazy<Delegate> _delegate;

		internal Lambda(Expression expression, ParserArguments parserArguments)
		{
			_expression = expression ?? throw new ArgumentNullException(nameof(expression));
			_parserArguments = parserArguments ?? throw new ArgumentNullException(nameof(parserArguments));

			// Note: I always lazy compile the generic lambda. Maybe in the future this can be a setting because if I generate a typed delegate this compilation is not required.
			_delegate = new Lazy<Delegate>(() =>
				Expression.Lambda(_expression, _parserArguments.UsedParameters.Select(p => p.Expression).ToArray()).Compile());
		}

		public Expression Expression { get { return _expression; } }
		public bool CaseInsensitive { get { return _parserArguments.Settings.CaseInsensitive; } }
		public string ExpressionText { get { return _parserArguments.ExpressionText; } }
		public Type ReturnType { get { return Expression.Type; } }

		/// <summary>
		/// Gets the parameters actually used in the expression parsed.
		/// </summary>
		/// <value>The used parameters.</value>
		[Obsolete("Use UsedParameters or DeclaredParameters")]
		public IEnumerable<Parameter> Parameters { get { return _parserArguments.UsedParameters; } }

		/// <summary>
		/// Gets the parameters actually used in the expression parsed.
		/// </summary>
		/// <value>The used parameters.</value>
		public IEnumerable<Parameter> UsedParameters { get { return _parserArguments.UsedParameters; } }
		/// <summary>
		/// Gets the parameters declared when parsing the expression.
		/// </summary>
		/// <value>The declared parameters.</value>
		public IEnumerable<Parameter> DeclaredParameters { get { return _parserArguments.DeclaredParameters; } }

		public IEnumerable<ReferenceType> Types { get { return _parserArguments.UsedTypes; } }
		public IEnumerable<Identifier> Identifiers { get { return _parserArguments.UsedIdentifiers; } }

		public object Invoke()
		{
			return InvokeWithUsedParameters(new object[0]);
		}

		public object Invoke(params Parameter[] parameters)
		{
			return Invoke((IEnumerable<Parameter>)parameters);
		}

		public object Invoke(IEnumerable<Parameter> parameters)
		{
			var args = (from usedParameter in UsedParameters
						from actualParameter in parameters
						where usedParameter.Name.Equals(actualParameter.Name, _parserArguments.Settings.KeyComparison)
						select actualParameter.Value)
				.ToArray();

			return InvokeWithUsedParameters(args);
		}

		/// <summary>
		/// Invoke the expression with the given parameters values.
		/// </summary>
		/// <param name="args">Order of parameters must be the same of the parameters used during parse (DeclaredParameters).</param>
		/// <returns></returns>
		public object Invoke(params object[] args)
		{
			var parameters = new List<Parameter>();
			var declaredParameters = DeclaredParameters.ToArray();

			int[] actualArgOrdering = null;
			object[] orderedArgs = args;
			var argsAreReordered = false;
			if (args != null && args.Length > 0)
			{
				if (declaredParameters.Length != args.Length)
					throw new InvalidOperationException("Arguments count mismatch.");

				actualArgOrdering = new int[args.Length];
				var usedParametersIndex = new Dictionary<string, int>(_parserArguments.Settings.KeyComparer);
				foreach (var v in UsedParameters)
				{
					if (declaredParameters.Any(x => string.Equals(x.Name, v.Name, _parserArguments.Settings.KeyComparison)))
					{
						usedParametersIndex[v.Name] = usedParametersIndex.Count;
					}
				}

				for (var i = 0; i < args.Length; i++)
				{
					if (!usedParametersIndex.TryGetValue(declaredParameters[i].Name, out var actualArgIndex))
					{
						actualArgIndex = -1;
					}
					if (actualArgIndex != i)
					{
						if (!argsAreReordered)
						{
							orderedArgs = (object[])orderedArgs.Clone();
							argsAreReordered = true;
						}
						if (actualArgIndex == -1)
						{
							Array.Resize(ref orderedArgs, args.Length - 1);
						}
						else
						{
							orderedArgs[actualArgIndex] = args[i];
						}
					}
					actualArgOrdering[i] = actualArgIndex;
				}
			}

			var result = InvokeWithUsedParameters(orderedArgs);
			if (argsAreReordered)
			{
				for (var i = 0; i < actualArgOrdering.Length; i++)
				{
					var pullFrom = actualArgOrdering[i];
					if (pullFrom >= 0)
					{
						args[i] = orderedArgs[pullFrom];
					}
				}
			}
			return result;
		}

		private object InvokeWithUsedParameters(object[] orderedArgs)
		{
			try
			{
				return _delegate.Value.DynamicInvoke(orderedArgs);
			}
			catch (TargetInvocationException exc)
			{
				if (exc.InnerException != null)
					ExceptionDispatchInfo.Capture(exc.InnerException).Throw();

				throw;
			}
		}

		public override string ToString()
		{
			return ExpressionText;
		}

		/// <summary>
		/// Generate the given delegate by compiling the lambda expression.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate to generate. Delegate parameters must match the one defined when creating the expression, see UsedParameters.</typeparam>
		public TDelegate Compile<TDelegate>()
		{
			var lambdaExpression = LambdaExpression<TDelegate>();
			return lambdaExpression.Compile();
		}

		[Obsolete("Use Compile<TDelegate>()")]
		public TDelegate Compile<TDelegate>(IEnumerable<Parameter> parameters)
		{
			var lambdaExpression = Expression.Lambda<TDelegate>(_expression, parameters.Select(p => p.Expression).ToArray());
			return lambdaExpression.Compile();
		}

		/// <summary>
		/// Generate a lambda expression.
		/// </summary>
		/// <returns>The lambda expression.</returns>
		/// <typeparam name="TDelegate">The delegate to generate. Delegate parameters must match the one defined when creating the expression, see UsedParameters.</typeparam>
		public Expression<TDelegate> LambdaExpression<TDelegate>()
		{
			return Expression.Lambda<TDelegate>(_expression, DeclaredParameters.Select(p => p.Expression).ToArray());
		}

		internal LambdaExpression LambdaExpression(Type delegateType)
		{
			var types = delegateType.GetGenericArguments();

			// return type
			types[types.Length - 1] = _expression.Type;

			var genericType = delegateType.GetGenericTypeDefinition();
			var inferredDelegateType = genericType.MakeGenericType(types);
			return Expression.Lambda(inferredDelegateType, _expression, DeclaredParameters.Select(p => p.Expression).ToArray());
		}
	}
}
