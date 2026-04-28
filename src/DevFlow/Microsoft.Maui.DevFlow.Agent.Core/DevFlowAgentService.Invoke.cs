using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.Json;

[assembly: MetadataUpdateHandler(typeof(Microsoft.Maui.DevFlow.Agent.Core.DevFlowActionHotReloadHandler))]

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Handles C# Hot Reload / Edit and Continue metadata updates.
/// When types are modified at runtime, this invalidates the cached
/// DevFlowAction list so newly added [DevFlowAction] methods are
/// immediately discoverable by AI agents.
/// </summary>
static class DevFlowActionHotReloadHandler
{
	/// <summary>Called by the runtime after a metadata update (Hot Reload / EnC).</summary>
	internal static void UpdateApplication(Type[]? updatedTypes)
	{
		// Any metadata update could have added/removed/changed [DevFlowAction] methods.
		// Invalidate so the next DiscoverActions() call rescans.
		DevFlowAgentService.InvalidateActionCache();
	}
}

// Invoke / reflection endpoints
public partial class DevFlowAgentService
{
	private static volatile Lazy<InvokeActionEntry[]> s_cachedActions = new(ScanActions, LazyThreadSafetyMode.ExecutionAndPublication);
	private static readonly ConcurrentDictionary<string, Type> s_typeResolutionCache = new(StringComparer.OrdinalIgnoreCase);

	#region Action Discovery

	private InvokeActionEntry[] DiscoverActions() => s_cachedActions.Value;

	/// <summary>
	/// Invalidates cached reflection data. The next call to DiscoverActions()
	/// will rescan all loaded assemblies, and type resolution will rescan as needed.
	/// Called by AssemblyLoad handler and MetadataUpdateHandler (Hot Reload).
	/// </summary>
	internal static void InvalidateActionCache()
	{
		s_cachedActions = new Lazy<InvokeActionEntry[]>(ScanActions, LazyThreadSafetyMode.ExecutionAndPublication);
		s_typeResolutionCache.Clear();
	}

	private void OnAssemblyLoaded(object? sender, AssemblyLoadEventArgs args)
	{
		if (args.LoadedAssembly.IsDynamic || IsFrameworkAssembly(args.LoadedAssembly))
			return;

		InvalidateActionCache();
	}

	private static InvokeActionEntry[] ScanActions()
	{
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

		// Detect and deduplicate shadowed action names (keep first occurrence)
		var duplicates = actions
			.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
			.Where(g => g.Count() > 1);

		foreach (var group in duplicates)
		{
			var shadowed = group.Skip(1);
			foreach (var dup in shadowed)
			{
				System.Diagnostics.Debug.WriteLine(
					$"[Microsoft.Maui.DevFlow] Warning: Duplicate DevFlowAction name '{group.Key}' on {dup.DeclaringType}.{dup.Method.Name} shadows the first registration on {group.First().DeclaringType}.{group.First().Method.Name}. The duplicate will be ignored.");
			}
		}

		return actions
			.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
			.Select(g => g.First())
			.ToArray();
	}

	private static readonly Lazy<HashSet<string>> s_trustedPlatformAssemblyNames = new(
		GetTrustedPlatformAssemblyNames,
		LazyThreadSafetyMode.ExecutionAndPublication);

	private static bool IsFrameworkAssembly(Assembly asm)
	{
		var name = asm.GetName().Name;
		if (string.IsNullOrEmpty(name))
			return true;

		return s_trustedPlatformAssemblyNames.Value.Contains(name)
			|| IsExplicitlyBlockedAssembly(name);
	}

	private static HashSet<string> GetTrustedPlatformAssemblyNames()
	{
		var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
		var assemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (string.IsNullOrEmpty(trustedPlatformAssemblies))
			return assemblyNames;

		// Determine the shared framework directory so we only treat assemblies
		// shipped with the runtime as "framework". TPA may also include app
		// assemblies (e.g. in test runners), which we must NOT filter out.
		var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);

		foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
		{
			// Only include assemblies that live in the shared framework directory
			if (runtimeDir != null && !path.StartsWith(runtimeDir, StringComparison.OrdinalIgnoreCase))
				continue;

			var assemblyName = Path.GetFileNameWithoutExtension(path);
			if (!string.IsNullOrEmpty(assemblyName))
				assemblyNames.Add(assemblyName);
		}

		return assemblyNames;
	}

	private static bool IsExplicitlyBlockedAssembly(string name)
	{
		return name.StartsWith("Fizzler", StringComparison.Ordinal)
			|| name.StartsWith("SkiaSharp", StringComparison.Ordinal)
			|| IsMicrosoftMauiFrameworkAssembly(name)
			|| IsDevFlowAssembly(name)
			|| name.StartsWith("Microsoft.CSharp", StringComparison.Ordinal)
			|| name.StartsWith("Microsoft.Win32", StringComparison.Ordinal);
	}

	private static bool IsMicrosoftMauiFrameworkAssembly(string name)
	{
		return string.Equals(name, "Microsoft.Maui", StringComparison.Ordinal)
			|| (name.StartsWith("Microsoft.Maui.", StringComparison.Ordinal)
				&& !name.StartsWith("Microsoft.Maui.DevFlow.", StringComparison.Ordinal));
	}

	private static bool IsDevFlowAssembly(string name)
	{
		return string.Equals(name, "Microsoft.Maui.DevFlow.Agent", StringComparison.Ordinal)
			|| string.Equals(name, "Microsoft.Maui.DevFlow.Agent.Core", StringComparison.Ordinal)
			|| string.Equals(name, "Microsoft.Maui.DevFlow.Agent.Gtk", StringComparison.Ordinal)
			|| string.Equals(name, "Microsoft.Maui.DevFlow.Blazor", StringComparison.Ordinal)
			|| string.Equals(name, "Microsoft.Maui.DevFlow.Blazor.Gtk", StringComparison.Ordinal)
			|| string.Equals(name, "Microsoft.Maui.DevFlow.Driver", StringComparison.Ordinal)
			|| string.Equals(name, "Microsoft.Maui.DevFlow.Logging", StringComparison.Ordinal);
	}

	private static InvokeParameterInfo[] BuildParameterInfoList(MethodInfo method)
	{
		return method.GetParameters().Select(p => new InvokeParameterInfo
		{
			Name = p.Name ?? "arg",
			Type = FormatParameterTypeName(p.ParameterType),
			Description = p.GetCustomAttribute<DescriptionAttribute>()?.Description,
			DefaultValue = p.HasDefaultValue ? FormatDefaultValue(p.DefaultValue) : null,
			IsRequired = !p.HasDefaultValue && Nullable.GetUnderlyingType(p.ParameterType) == null
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
		if (s_typeResolutionCache.TryGetValue(typeName, out var cached))
			return cached;

		// Try fully-qualified name first
		var type = Type.GetType(typeName);
		if (type != null)
		{
			if (IsFrameworkAssembly(type.Assembly))
				type = null;
			else
			{
				s_typeResolutionCache.TryAdd(typeName, type);
				return type;
			}
		}

		// Scan loaded assemblies
		var matches = new List<Type>();
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			if (asm.IsDynamic || IsFrameworkAssembly(asm)) continue;

			// Full name match (preferred)
			type = asm.GetType(typeName, throwOnError: false, ignoreCase: true);
			if (type != null)
			{
				s_typeResolutionCache.TryAdd(typeName, type);
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
							matches.Add(t);
					}
				}
				catch { }
			}
		}

		// Deduplicate: if all matches refer to the same type, use it
		var distinct = matches.Select(t => t.FullName).Distinct().ToList();
		if (distinct.Count == 1)
		{
			s_typeResolutionCache.TryAdd(typeName, matches[0]);
			return matches[0];
		}

		if (distinct.Count > 1)
		{
			System.Diagnostics.Debug.WriteLine(
				$"[Microsoft.Maui.DevFlow] Warning: Ambiguous type name '{typeName}' matched {distinct.Count} types: {string.Join(", ", distinct)}. Use a fully-qualified type name to resolve the ambiguity.");
		}

		return null;
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
			if (argElement.ValueKind != JsonValueKind.String)
				throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");
			var str = argElement.GetString()
				?? throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");
			return bool.Parse(str);
		}

		// Integer types
		if (underlying == typeof(int)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetInt32() : argElement.ValueKind == JsonValueKind.String ? int.Parse(argElement.GetString()!) : throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");
		if (underlying == typeof(long)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetInt64() : argElement.ValueKind == JsonValueKind.String ? long.Parse(argElement.GetString()!) : throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");
		if (underlying == typeof(short)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetInt16() : argElement.ValueKind == JsonValueKind.String ? short.Parse(argElement.GetString()!) : throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");
		if (underlying == typeof(byte)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetByte() : argElement.ValueKind == JsonValueKind.String ? byte.Parse(argElement.GetString()!) : throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");

		// Floating point
		if (underlying == typeof(float)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetSingle() : argElement.ValueKind == JsonValueKind.String ? float.Parse(argElement.GetString()!) : throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");
		if (underlying == typeof(double)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetDouble() : argElement.ValueKind == JsonValueKind.String ? double.Parse(argElement.GetString()!) : throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");
		if (underlying == typeof(decimal)) return argElement.ValueKind == JsonValueKind.Number ? argElement.GetDecimal() : argElement.ValueKind == JsonValueKind.String ? decimal.Parse(argElement.GetString()!) : throw new ArgumentException($"Cannot convert {argElement.ValueKind} to {underlying.Name}");

		// Enums
		if (underlying.IsEnum)
		{
			if (argElement.ValueKind == JsonValueKind.String)
			{
				var s = argElement.GetString() ?? throw new ArgumentException($"Cannot convert null string to {underlying.Name}");
				return Enum.Parse(underlying, s, ignoreCase: true);
			}
			if (argElement.ValueKind == JsonValueKind.Number)
				return Enum.ToObject(underlying, argElement.GetInt64());
			throw new ArgumentException($"Cannot convert {argElement.ValueKind} to enum {underlying.Name}");
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
				if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>) || def == typeof(ICollection<>) || def == typeof(IReadOnlyCollection<>))
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

	private static int? ScoreInvokeCandidate(MethodInfo method, JsonElement[]? args)
	{
		var parameters = method.GetParameters();
		var argCount = args?.Length ?? 0;
		var required = parameters.Count(p => !p.HasDefaultValue);
		if (argCount < required || argCount > parameters.Length)
			return null;

		var score = argCount == parameters.Length ? 1 : 0;
		for (var i = 0; i < argCount; i++)
		{
			var argScore = ScoreInvokeArg(parameters[i].ParameterType, args![i]);
			if (argScore == null)
				return null;

			score += argScore.Value;
		}

		return score;
	}

	private static int? ScoreInvokeArg(Type targetType, JsonElement argElement)
	{
		var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

		if (argElement.ValueKind == JsonValueKind.Null)
			return Nullable.GetUnderlyingType(targetType) != null || !targetType.IsValueType ? 6 : null;

		if (underlying == typeof(string))
			return argElement.ValueKind == JsonValueKind.String ? 6 : null;

		if (underlying == typeof(bool))
			return argElement.ValueKind switch
			{
				JsonValueKind.True or JsonValueKind.False => 6,
				JsonValueKind.String => bool.TryParse(argElement.GetString(), out _) ? 2 : null,
				_ => null
			};

		var numericScore = ScoreNumericInvokeArg(underlying, argElement);
		if (numericScore != null)
			return numericScore;

		if (underlying.IsEnum)
		{
			if (argElement.ValueKind == JsonValueKind.String)
				return Enum.TryParse(underlying, argElement.GetString(), ignoreCase: true, out _) ? 4 : null;
			if (argElement.ValueKind == JsonValueKind.Number)
				return argElement.TryGetInt64(out _) ? 2 : null;
			return null;
		}

		if (argElement.ValueKind == JsonValueKind.Array && TryGetInvokeCollectionElementType(underlying, out var elementType))
		{
			var score = 3;
			foreach (var item in argElement.EnumerateArray())
			{
				var itemScore = ScoreInvokeArg(elementType, item);
				if (itemScore == null)
					return null;
				score += Math.Min(itemScore.Value, 4);
			}
			return score;
		}

		return underlying == typeof(object) && argElement.ValueKind == JsonValueKind.String ? 1 : null;
	}

	private static int? ScoreNumericInvokeArg(Type underlying, JsonElement argElement)
	{
		if (argElement.ValueKind == JsonValueKind.Number)
		{
			if (underlying == typeof(int)) return argElement.TryGetInt32(out _) ? 6 : null;
			if (underlying == typeof(long)) return argElement.TryGetInt64(out _) ? 6 : null;
			if (underlying == typeof(short)) return argElement.TryGetInt16(out _) ? 6 : null;
			if (underlying == typeof(byte)) return argElement.TryGetByte(out _) ? 6 : null;
			if (underlying == typeof(float)) return argElement.TryGetSingle(out _) ? 6 : null;
			if (underlying == typeof(double)) return argElement.TryGetDouble(out _) ? 6 : null;
			if (underlying == typeof(decimal)) return argElement.TryGetDecimal(out _) ? 6 : null;
		}

		if (argElement.ValueKind != JsonValueKind.String)
			return null;

		var value = argElement.GetString();
		if (underlying == typeof(int)) return int.TryParse(value, out _) ? 2 : null;
		if (underlying == typeof(long)) return long.TryParse(value, out _) ? 2 : null;
		if (underlying == typeof(short)) return short.TryParse(value, out _) ? 2 : null;
		if (underlying == typeof(byte)) return byte.TryParse(value, out _) ? 2 : null;
		if (underlying == typeof(float)) return float.TryParse(value, out _) ? 2 : null;
		if (underlying == typeof(double)) return double.TryParse(value, out _) ? 2 : null;
		if (underlying == typeof(decimal)) return decimal.TryParse(value, out _) ? 2 : null;

		return null;
	}

	private static bool TryGetInvokeCollectionElementType(Type type, out Type elementType)
	{
		if (type.IsArray)
		{
			elementType = type.GetElementType()!;
			return true;
		}

		if (type.IsGenericType)
		{
			var def = type.GetGenericTypeDefinition();
			if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyList<>) || def == typeof(ICollection<>) || def == typeof(IReadOnlyCollection<>))
			{
				elementType = type.GetGenericArguments()[0];
				return true;
			}
		}

		elementType = typeof(object);
		return false;
	}

	private static string FormatInvokeMethodSignature(MethodInfo method) =>
		$"{method.Name}({string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})";

	#endregion

	#region Invoke Execution

	private static async Task<(bool success, string? returnValue, string? returnType, string? error)> InvokeMethodAsync(
		MethodInfo method, object? target, object?[] args)
	{
		try
		{
			var result = method.Invoke(target, args);

			// Handle ValueTask (struct — does not inherit from Task)
			if (result is ValueTask vt)
			{
				await vt;
				return (true, null, "void", null);
			}

			// Handle ValueTask<T> via reflection (generic struct)
			var resultType = result?.GetType();
			if (resultType != null && resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
			{
				var asTaskMethod = resultType.GetMethod("AsTask");
				var task2 = (Task)asTaskMethod!.Invoke(result, null)!;
				await task2;
				var resultProp = task2.GetType().GetProperty("Result");
				var taskResult = resultProp?.GetValue(task2);
				var innerType = resultType.GetGenericArguments()[0];
				return (true, taskResult != null ? FormatPropertyValue(taskResult) : null, FormatParameterTypeName(innerType), null);
			}

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

	private async Task<InvokeMethodResult> DispatchInvokeMethodAsync(MethodInfo method, object? target, object?[] args)
	{
		// Force the async DispatchAsync overload so invoke continuations are awaited by the dispatcher callback.
		var result = await DispatchAsync<InvokeMethodResult>(async () =>
		{
			var (success, returnValue, returnType, error) = await InvokeMethodAsync(method, target, args);
			return new InvokeMethodResult(success, returnValue, returnType, error);
		});
		return result!;
	}

	private static bool IsElementInvokeAllowedMethod(MethodInfo method)
	{
		var declaringType = method.DeclaringType;
		if (declaringType == null || IsFrameworkAssembly(declaringType.Assembly))
			return false;

		var assemblyName = declaringType.Assembly.GetName().Name;
		return !string.Equals(assemblyName, "Microsoft.Maui", StringComparison.Ordinal)
			&& (assemblyName == null
				|| !assemblyName.StartsWith("Microsoft.Maui.", StringComparison.Ordinal)
				|| assemblyName.StartsWith("Microsoft.Maui.DevFlow.", StringComparison.Ordinal));
	}

	#endregion

	#region HTTP Handlers

	private static HttpResponse InvokeError(string error) =>
		HttpResponse.Error(error);

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
			var invokeTask = await DispatchAsync(() => InvokeMethodAsync(action.Method, null, convertedArgs));
			var (success, returnValue, returnType, error) = await invokeTask;

			return success
				? HttpResponse.Json(new { success = true, action = action.Name, returnValue, returnType })
				: InvokeError($"Action '{actionName}' failed: {error}");
		}
		catch (Exception ex)
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

		// Always enumerate methods by name to avoid AmbiguousMatchException on overloads
		var candidates = type.GetMethods(bindingFlags)
			.Where(m => string.Equals(m.Name, body.MethodName, StringComparison.OrdinalIgnoreCase))
			.ToArray();

		if (candidates.Length == 0)
			return InvokeError($"Method '{body.MethodName}' not found on type '{type.FullName}'.");

		var argCount = body.Args?.Length ?? 0;
		var scored = candidates
			.Select(m => new { Method = m, Score = ScoreInvokeCandidate(m, body.Args) })
			.Where(m => m.Score != null)
			.ToArray();

		if (scored.Length == 0)
		{
			var signatures = string.Join(", ", candidates.Select(FormatInvokeMethodSignature));
			return InvokeError($"No overload of '{body.MethodName}' on type '{type.FullName}' matches {argCount} argument(s). Candidates: {signatures}");
		}

		var bestScore = scored.Max(m => m.Score!.Value);
		var matched = scored.Where(m => m.Score == bestScore).Select(m => m.Method).ToArray();

		if (matched.Length > 1)
		{
			var signatures = string.Join(", ", matched.Select(FormatInvokeMethodSignature));
			return InvokeError($"Ambiguous method '{body.MethodName}' on type '{type.FullName}' - {matched.Length} overloads match {argCount} argument(s). Use a fully-qualified type or adjust arguments. Candidates: {signatures}");
		}

		var method = matched[0];

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
				(success, returnValue, returnType, error) = await DispatchInvokeMethodAsync(method, target, convertedArgs);
			}
			else
			{
				var invokeTask = await DispatchAsync(() => InvokeMethodAsync(method, target, convertedArgs));
				(success, returnValue, returnType, error) = await invokeTask;
			}

			return success
				? HttpResponse.Json(new { success = true, typeName = type.FullName, methodName = method.Name, returnValue, returnType })
				: InvokeError($"Invoke failed: {error}");
		}
		catch (Exception ex)
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

		// Resolve element and method on the UI thread
		var resolution = await DispatchAsync(() =>
		{
			var el = _treeWalker.GetElementById(id, _app);
			if (el == null)
				return (element: (object?)null, method: (MethodInfo?)null, error: (string?)$"Element '{id}' not found");

			var type = el.GetType();
			var method = type.GetMethod(body.MethodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
			if (method == null)
				return (element: (object?)null, method: (MethodInfo?)null, error: (string?)$"Method '{body.MethodName}' not found on element type '{type.Name}'");
			if (!IsElementInvokeAllowedMethod(method))
				return (element: (object?)null, method: (MethodInfo?)null, error: (string?)$"Method '{body.MethodName}' on element type '{type.Name}' is not invocable because it is declared by framework type '{method.DeclaringType?.FullName}'.");

			return (element: (object?)el, method: (MethodInfo?)method, error: (string?)null);
		});

		if (resolution.error != null)
			return InvokeError(resolution.error);

		try
		{
			var convertedArgs = ConvertInvokeArgs(resolution.method!.GetParameters(), body.Args);

			// Invoke on the UI thread and await the result (handles both sync and async methods)
			var invokeTask = await DispatchAsync(() => InvokeMethodAsync(resolution.method!, resolution.element, convertedArgs));
			var (success, returnValue, returnType, error) = await invokeTask;

			return success
				? HttpResponse.Json(new { success = true, elementId = id, methodName = body.MethodName, returnValue, returnType })
				: InvokeError($"Element invoke failed: {error}");
		}
		catch (ArgumentException ex)
		{
			return InvokeError($"Argument error: {ex.Message}");
		}
		catch (Exception ex)
		{
			return InvokeError($"Element invoke failed: {ex.Message}");
		}
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
					isAsync = typeof(Task).IsAssignableFrom(m.ReturnType)
					|| m.ReturnType == typeof(ValueTask)
					|| (m.ReturnType.IsGenericType && m.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)),
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

	private sealed record InvokeMethodResult(bool Success, string? ReturnValue, string? ReturnType, string? Error);

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
