using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Agent.Core;

// Invoke / reflection endpoints
public partial class DevFlowAgentService
{
	private InvokeActionEntry[]? _cachedActions;
	private readonly Dictionary<string, Type> _typeResolutionCache = new(StringComparer.OrdinalIgnoreCase);

	#region Action Discovery

	private InvokeActionEntry[] DiscoverActions()
	{
		if (_cachedActions != null)
			return _cachedActions;

		var actions = new List<InvokeActionEntry>();

		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			if (asm.IsDynamic || IsFrameworkAssembly(asm))
				continue;

			Type[] types;
			try { types = asm.GetTypes(); }
			catch { continue; }

			foreach (var type in types)
			{
				foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
				{
					var attr = method.GetCustomAttribute<DevFlowActionAttribute>();
					if (attr == null) continue;

					actions.Add(new InvokeActionEntry
					{
						Name = attr.Name,
						Description = attr.Description,
						DeclaringType = type.FullName ?? type.Name,
						Method = method,
						Parameters = BuildParameterInfoList(method)
					});
				}
			}
		}

		_cachedActions = actions.ToArray();
		return _cachedActions;
	}

	private static bool IsFrameworkAssembly(Assembly asm)
	{
		var name = asm.GetName().Name;
		if (name == null) return true;
		return name.StartsWith("System", StringComparison.Ordinal)
			|| name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal)
			|| name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
			|| name.StartsWith("netstandard", StringComparison.Ordinal)
			|| name.StartsWith("mscorlib", StringComparison.Ordinal)
			|| name.StartsWith("Fizzler", StringComparison.Ordinal)
			|| name.StartsWith("SkiaSharp", StringComparison.Ordinal);
	}

	private static InvokeParameterInfo[] BuildParameterInfoList(MethodInfo method)
	{
		return method.GetParameters().Select(p => new InvokeParameterInfo
		{
			Name = p.Name ?? "arg",
			Type = FormatParameterTypeName(p.ParameterType),
			Description = p.GetCustomAttribute<DescriptionAttribute>()?.Description,
			DefaultValue = p.HasDefaultValue ? FormatDefaultValue(p.DefaultValue) : null,
			IsRequired = !p.HasDefaultValue && !p.ParameterType.IsAssignableTo(typeof(Nullable<>))
		}).ToArray();
	}

	private static string FormatParameterTypeName(Type type)
	{
		var underlying = Nullable.GetUnderlyingType(type);
		if (underlying != null)
			return FormatParameterTypeName(underlying) + "?";

		if (type.IsArray)
			return FormatParameterTypeName(type.GetElementType()!) + "[]";

		if (type.IsGenericType)
		{
			var def = type.GetGenericTypeDefinition();
			if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>))
				return FormatParameterTypeName(type.GetGenericArguments()[0]) + "[]";
		}

		return Type.GetTypeCode(type) switch
		{
			TypeCode.String => "string",
			TypeCode.Boolean => "bool",
			TypeCode.Int32 => "int",
			TypeCode.Int64 => "long",
			TypeCode.Int16 => "short",
			TypeCode.Byte => "byte",
			TypeCode.Single => "float",
			TypeCode.Double => "double",
			TypeCode.Decimal => "decimal",
			_ => type.IsEnum ? $"enum({type.Name})" : type.Name
		};
	}

	private static string? FormatDefaultValue(object? value)
	{
		if (value == null) return "null";
		if (value is string s) return s;
		if (value is bool b) return b ? "true" : "false";
		return value.ToString();
	}

	#endregion

	#region Type Resolution

	private Type? ResolveType(string typeName)
	{
		if (_typeResolutionCache.TryGetValue(typeName, out var cached))
			return cached;

		// Try fully-qualified name first
		var type = Type.GetType(typeName);
		if (type != null)
		{
			_typeResolutionCache[typeName] = type;
			return type;
		}

		// Scan loaded assemblies
		Type? bestMatch = null;
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			if (asm.IsDynamic) continue;

			// Full name match (preferred)
			type = asm.GetType(typeName, throwOnError: false, ignoreCase: true);
			if (type != null)
			{
				_typeResolutionCache[typeName] = type;
				return type;
			}

			// Simple name match (fallback for unqualified names)
			if (!typeName.Contains('.'))
			{
				try
				{
					foreach (var t in asm.GetTypes())
					{
						if (string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase))
						{
							if (IsFrameworkAssembly(asm))
								continue; // prefer app types over framework types
							bestMatch = t;
						}
					}
				}
				catch { }
			}
		}

		if (bestMatch != null)
			_typeResolutionCache[typeName] = bestMatch;

		return bestMatch;
	}

	#endregion

	#region Parameter Conversion

	private static object? ConvertInvokeArg(Type targetType, JsonElement argElement)
	{
		var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

		// Null handling
		if (argElement.ValueKind == JsonValueKind.Null)
		{
			if (Nullable.GetUnderlyingType(targetType) != null || !targetType.IsValueType)
				return null;
			throw new ArgumentException($"Cannot pass null for non-nullable type {targetType.Name}");
		}

		// String
		if (underlying == typeof(string))
			return argElement.GetString();

		// Boolean
		if (underlying == typeof(bool))
		{
			if (argElement.ValueKind == JsonValueKind.True || argElement.ValueKind == JsonValueKind.False)
				return argElement.GetBoolean();
			return bool.Parse(argElement.GetString()!);
		}

		// Integer types
		if (underlying == typeof(int)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetInt32() : int.Parse(argElement.GetString()!);
		if (underlying == typeof(long)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetInt64() : long.Parse(argElement.GetString()!);
		if (underlying == typeof(short)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetInt16() : short.Parse(argElement.GetString()!);
		if (underlying == typeof(byte)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetByte() : byte.Parse(argElement.GetString()!);

		// Floating point
		if (underlying == typeof(float)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetSingle() : float.Parse(argElement.GetString()!);
		if (underlying == typeof(double)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetDouble() : double.Parse(argElement.GetString()!);
		if (underlying == typeof(decimal)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetDecimal() : decimal.Parse(argElement.GetString()!);

		// Enums
		if (underlying.IsEnum)
		{
			var s = argElement.GetString() ?? argElement.GetRawText();
			return Enum.Parse(underlying, s, ignoreCase: true);
		}

		// Arrays and lists
		if (argElement.ValueKind == JsonValueKind.Array)
		{
			Type? elementType = null;

			if (underlying.IsArray)
				elementType = underlying.GetElementType()!;
			else if (underlying.IsGenericType)
			{
				var def = underlying.GetGenericTypeDefinition();
				if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>))
					elementType = underlying.GetGenericArguments()[0];
			}

			if (elementType != null)
			{
				var items = new List<object?>();
				foreach (var item in argElement.EnumerateArray())
					items.Add(ConvertInvokeArg(elementType, item));

				if (underlying.IsArray)
				{
					var arr = Array.CreateInstance(elementType, items.Count);
					for (int i = 0; i < items.Count; i++)
						arr.SetValue(items[i], i);
					return arr;
				}

				var listType = typeof(List<>).MakeGenericType(elementType);
				var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
				foreach (var item in items)
					list.Add(item);
				return list;
			}
		}

		// Fallback: treat as string
		if (argElement.ValueKind == JsonValueKind.String)
			return argElement.GetString();

		throw new ArgumentException($"Cannot convert JSON {argElement.ValueKind} to {targetType.Name}");
	}

	private static object?[] ConvertInvokeArgs(ParameterInfo[] parameters, JsonElement[]? args)
	{
		var result = new object?[parameters.Length];
		for (int i = 0; i < parameters.Length; i++)
		{
			if (args != null && i < args.Length)
			{
				result[i] = ConvertInvokeArg(parameters[i].ParameterType, args[i]);
			}
			else if (parameters[i].HasDefaultValue)
			{
				result[i] = parameters[i].DefaultValue;
			}
			else
			{
				throw new ArgumentException($"Missing required argument '{parameters[i].Name}' (parameter {i})");
			}
		}
		return result;
	}

	#endregion

	#region Invoke Execution

	private static async Task<(bool success, string? returnValue, string? returnType, string? error)> InvokeMethodAsync(
		MethodInfo method, object? target, object?[] args)
	{
		try
		{
			var result = method.Invoke(target, args);

			// Handle async methods
			if (result is Task task)
			{
				await task;

				var taskType = task.GetType();
				if (taskType.IsGenericType)
				{
					// Task<T> — unwrap the result
					var resultProp = taskType.GetProperty("Result");
					var taskResult = resultProp?.GetValue(task);
					return (true, FormatPropertyValue(taskResult), FormatParameterTypeName(taskType.GetGenericArguments()[0]), null);
				}

				return (true, null, "void", null);
			}

			// Void method
			if (method.ReturnType == typeof(void))
				return (true, null, "void", null);

			// Synchronous return value
			return (true, FormatPropertyValue(result), FormatParameterTypeName(method.ReturnType), null);
		}
		catch (TargetInvocationException tie)
		{
			var inner = tie.InnerException ?? tie;
			return (false, null, null, $"{inner.GetType().Name}: {inner.Message}");
		}
		catch (Exception ex)
		{
			return (false, null, null, $"{ex.GetType().Name}: {ex.Message}");
		}
	}

	#endregion

	#region HTTP Handlers

	private static HttpResponse InvokeError(string error) =>
		HttpResponse.Json(new { success = false, error });

	private Task<HttpResponse> HandleListActions(HttpRequest request)
	{
		var actions = DiscoverActions();
		var result = actions.Select(a => new
		{
			name = a.Name,
			description = a.Description,
			declaringType = a.DeclaringType,
			parameters = a.Parameters.Select(p => new
			{
				name = p.Name,
				type = p.Type,
				description = p.Description,
				defaultValue = p.DefaultValue,
				isRequired = p.IsRequired
			})
		});
		return Task.FromResult(HttpResponse.Json(new { actions = result }));
	}

	private async Task<HttpResponse> HandleInvokeAction(HttpRequest request)
	{
		if (!request.RouteParams.TryGetValue("name", out var actionName))
			return InvokeError("Action name required");

		var actions = DiscoverActions();
		var action = Array.Find(actions, a => string.Equals(a.Name, actionName, StringComparison.OrdinalIgnoreCase));
		if (action == null)
			return InvokeError($"Action '{actionName}' not found. Use GET /api/v1/invoke/actions to list available actions.");

		JsonElement[]? args = null;
		if (request.Body != null)
		{
			var body = request.BodyAs<InvokeActionRequest>();
			args = body?.Args;
		}

		try
		{
			var convertedArgs = ConvertInvokeArgs(action.Method.GetParameters(), args);
			var (success, returnValue, returnType, error) = await InvokeMethodAsync(action.Method, null, convertedArgs);

			return success
				? HttpResponse.Json(new { success = true, action = action.Name, returnValue, returnType })
				: InvokeError($"Action '{actionName}' failed: {error}");
		}
		catch (ArgumentException ex)
		{
			return InvokeError($"Argument error: {ex.Message}");
		}
	}

	private async Task<HttpResponse> HandleInvoke(HttpRequest request)
	{
		var body = request.BodyAs<InvokeRequest>();
		if (body?.TypeName == null)
			return InvokeError("typeName is required");
		if (body.MethodName == null)
			return InvokeError("methodName is required");

		var type = ResolveType(body.TypeName);
		if (type == null)
			return InvokeError($"Type '{body.TypeName}' not found in loaded assemblies.");

		var resolve = body.Resolve ?? "static";
		var isService = string.Equals(resolve, "service", StringComparison.OrdinalIgnoreCase);

		var bindingFlags = BindingFlags.Public | BindingFlags.IgnoreCase
			| (isService ? BindingFlags.Instance : BindingFlags.Static);

		var method = type.GetMethod(body.MethodName, bindingFlags);
		if (method == null)
		{
			// Try finding by parameter count for overload resolution
			var candidates = type.GetMethods(bindingFlags)
				.Where(m => string.Equals(m.Name, body.MethodName, StringComparison.OrdinalIgnoreCase))
				.ToArray();

			if (candidates.Length == 0)
				return InvokeError($"Method '{body.MethodName}' not found on type '{type.FullName}'.");

			var argCount = body.Args?.Length ?? 0;
			method = candidates.FirstOrDefault(m =>
			{
				var ps = m.GetParameters();
				var required = ps.Count(p => !p.HasDefaultValue);
				return argCount >= required && argCount <= ps.Length;
			}) ?? candidates[0];
		}

		object? target = null;
		if (isService)
		{
			target = await DispatchAsync(() =>
			{
				var sp = _app?.Handler?.MauiContext?.Services;
				return sp?.GetService(type);
			});

			if (target == null)
				return InvokeError($"Could not resolve type '{type.FullName}' from DI container. Ensure it is registered in the app's service collection.");
		}

		try
		{
			var convertedArgs = ConvertInvokeArgs(method.GetParameters(), body.Args);

			bool success; string? returnValue; string? returnType; string? error;
			if (isService)
			{
				// Service invoke must run on UI thread since the service may access UI state
				var invokeTask = await DispatchAsync(() => InvokeMethodAsync(method, target, convertedArgs));
				(success, returnValue, returnType, error) = await invokeTask;
			}
			else
			{
				(success, returnValue, returnType, error) = await InvokeMethodAsync(method, target, convertedArgs);
			}

			return success
				? HttpResponse.Json(new { success = true, typeName = type.FullName, methodName = method.Name, returnValue, returnType })
				: InvokeError($"Invoke failed: {error}");
		}
		catch (ArgumentException ex)
		{
			return InvokeError($"Argument error: {ex.Message}");
		}
	}

	private async Task<HttpResponse> HandleElementInvoke(HttpRequest request)
	{
		if (_app == null) return InvokeError("Agent not bound to app");
		if (!request.RouteParams.TryGetValue("id", out var id))
			return InvokeError("Element ID required");

		var body = request.BodyAs<ElementInvokeRequest>();
		if (body?.MethodName == null)
			return InvokeError("methodName is required");

		var result = await DispatchAsync(() =>
		{
			var el = _treeWalker.GetElementById(id, _app);
			if (el == null) return (found: false, success: false, returnValue: (string?)null, returnType: (string?)null, error: (string?)$"Element '{id}' not found");

			var type = el.GetType();
			var method = type.GetMethod(body.MethodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
			if (method == null)
				return (found: false, success: false, returnValue: (string?)null, returnType: (string?)null, error: (string?)$"Method '{body.MethodName}' not found on element type '{type.Name}'");

			try
			{
				var convertedArgs = ConvertInvokeArgs(method.GetParameters(), body.Args);
				var invokeResult = method.Invoke(el, convertedArgs);

				if (invokeResult is Task)
					return (found: true, success: true, returnValue: (string?)null, returnType: (string?)"Task", error: (string?)"ASYNC_NEEDS_AWAIT");

				if (method.ReturnType == typeof(void))
					return (found: true, success: true, returnValue: (string?)null, returnType: (string?)"void", error: (string?)null);

				return (found: true, success: true, returnValue: FormatPropertyValue(invokeResult), returnType: (string?)FormatParameterTypeName(method.ReturnType), error: (string?)null);
			}
			catch (TargetInvocationException tie)
			{
				var inner = tie.InnerException ?? tie;
				return (found: true, success: false, returnValue: (string?)null, returnType: (string?)null, error: (string?)$"{inner.GetType().Name}: {inner.Message}");
			}
			catch (ArgumentException ex)
			{
				return (found: true, success: false, returnValue: (string?)null, returnType: (string?)null, error: (string?)$"Argument error: {ex.Message}");
			}
		});

		// Handle async methods that need to be awaited off the UI thread
		if (result.error == "ASYNC_NEEDS_AWAIT")
		{
			try
			{
				var el = await DispatchAsync(() => _treeWalker.GetElementById(id, _app!));
				if (el == null) return InvokeError($"Element '{id}' not found");

				var type = el.GetType();
				var method = type.GetMethod(body.MethodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;
				var convertedArgs = ConvertInvokeArgs(method.GetParameters(), body.Args);
				var invokeTask = await DispatchAsync(() => InvokeMethodAsync(method, el, convertedArgs));
				var (success, returnValue, returnType, error) = await invokeTask;

				return success
					? HttpResponse.Json(new { success = true, elementId = id, methodName = body.MethodName, returnValue, returnType })
					: InvokeError($"Element invoke failed: {error}");
			}
			catch (Exception ex)
			{
				return InvokeError($"Element invoke failed: {ex.Message}");
			}
		}

		if (!result.found)
			return InvokeError(result.error ?? "Not found");

		return result.success
			? HttpResponse.Json(new { success = true, elementId = id, methodName = body.MethodName, returnValue = result.returnValue, returnType = result.returnType })
			: InvokeError(result.error ?? "Invoke failed");
	}

	private Task<HttpResponse> HandleListMethods(HttpRequest request)
	{
		if (!request.QueryParams.TryGetValue("typeName", out var typeName) || string.IsNullOrWhiteSpace(typeName))
			return Task.FromResult(HttpResponse.Error("Query parameter 'typeName' is required"));

		var type = ResolveType(typeName);
		if (type == null)
			return Task.FromResult(HttpResponse.NotFound($"Type '{typeName}' not found in loaded assemblies."));

		var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
			.Where(m => !m.IsSpecialName) // exclude property getters/setters, event add/remove
			.Select(m =>
			{
				var actionAttr = m.GetCustomAttribute<DevFlowActionAttribute>();
				return new
				{
					name = m.Name,
					returnType = FormatParameterTypeName(m.ReturnType),
					isStatic = m.IsStatic,
					isAsync = typeof(Task).IsAssignableFrom(m.ReturnType),
					devFlowActionName = actionAttr?.Name,
					parameters = m.GetParameters().Select(p => new
					{
						name = p.Name,
						type = FormatParameterTypeName(p.ParameterType),
						description = p.GetCustomAttribute<DescriptionAttribute>()?.Description,
						defaultValue = p.HasDefaultValue ? FormatDefaultValue(p.DefaultValue) : null,
						isRequired = !p.HasDefaultValue
					})
				};
			});

		return Task.FromResult(HttpResponse.Json(new { typeName = type.FullName, methods }));
	}

	#endregion

	#region DTOs

	private class InvokeActionEntry
	{
		public string Name { get; set; } = "";
		public string? Description { get; set; }
		public string DeclaringType { get; set; } = "";
		public MethodInfo Method { get; set; } = null!;
		public InvokeParameterInfo[] Parameters { get; set; } = [];
	}

	private class InvokeParameterInfo
	{
		public string Name { get; set; } = "";
		public string Type { get; set; } = "";
		public string? Description { get; set; }
		public string? DefaultValue { get; set; }
		public bool IsRequired { get; set; }
	}

	#endregion
}

public class InvokeRequest
{
	public string? TypeName { get; set; }
	public string? MethodName { get; set; }
	public JsonElement[]? Args { get; set; }
	public string? Resolve { get; set; }
}

public class InvokeActionRequest
{
	public JsonElement[]? Args { get; set; }
}

public class ElementInvokeRequest
{
	public string? MethodName { get; set; }
	public JsonElement[]? Args { get; set; }
}
