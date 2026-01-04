
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace CoreRemoting.Serialization.NeoBinary
{
	partial class NeoBinarySerializer
	{
		private void SerializeExpression(Expression expression, BinaryWriter writer, HashSet<object> serializedObjects,
			Dictionary<object, int> objectMap)
		{
			if (expression == null)
			{
				writer.Write((byte)0); // Null expression
				return;
			}

			writer.Write((byte)1); // Expression marker

			// Write NodeType
			writer.Write((int)expression.NodeType);

			// Write Type
			WriteTypeInfo(writer, expression.Type);

			// Write expression-specific data based on NodeType
			switch (expression.NodeType)
			{
				case ExpressionType.Constant:
					var constExpr = (ConstantExpression)expression;
					SerializeObject(constExpr.Value, writer, serializedObjects, objectMap);
					break;

				case ExpressionType.Parameter:
					var paramExpr = (ParameterExpression)expression;
					writer.Write(paramExpr.Name ?? string.Empty);
					break;

				case ExpressionType.MemberAccess:
					var memberExpr = (MemberExpression)expression;
					writer.Write(memberExpr.Member.Name);
					writer.Write(memberExpr.Member.MemberType.ToString());
					SerializeExpression(memberExpr.Expression, writer, serializedObjects, objectMap);
					break;

				case ExpressionType.Call:
					var callExpr = (MethodCallExpression)expression;
					SerializeObject(callExpr.Method, writer, serializedObjects, objectMap);
					SerializeExpression(callExpr.Object, writer, serializedObjects, objectMap);
					writer.Write(callExpr.Arguments.Count);
					foreach (var arg in callExpr.Arguments)
					{
						SerializeExpression(arg, writer, serializedObjects, objectMap);
					}

					break;

				case ExpressionType.Lambda:
					var lambdaExpr = (LambdaExpression)expression;
					writer.Write(lambdaExpr.Name ?? string.Empty);
					writer.Write(lambdaExpr.TailCall);
					SerializeExpression(lambdaExpr.Body, writer, serializedObjects, objectMap);
					writer.Write(lambdaExpr.Parameters.Count);
					foreach (var param in lambdaExpr.Parameters)
					{
						SerializeExpression(param, writer, serializedObjects, objectMap);
					}

					break;

				case ExpressionType.Add:
				case ExpressionType.AddChecked:
				case ExpressionType.And:
				case ExpressionType.AndAlso:
				case ExpressionType.ArrayIndex:
				case ExpressionType.Coalesce:
				case ExpressionType.Divide:
				case ExpressionType.Equal:
				case ExpressionType.ExclusiveOr:
				case ExpressionType.GreaterThan:
				case ExpressionType.GreaterThanOrEqual:
				case ExpressionType.LeftShift:
				case ExpressionType.LessThan:
				case ExpressionType.LessThanOrEqual:
				case ExpressionType.Modulo:
				case ExpressionType.Multiply:
				case ExpressionType.MultiplyChecked:
				case ExpressionType.NotEqual:
				case ExpressionType.Or:
				case ExpressionType.OrElse:
				case ExpressionType.Power:
				case ExpressionType.RightShift:
				case ExpressionType.Subtract:
				case ExpressionType.SubtractChecked:
					var binaryExpr = (BinaryExpression)expression;
					SerializeExpression(binaryExpr.Left, writer, serializedObjects, objectMap);
					SerializeExpression(binaryExpr.Right, writer, serializedObjects, objectMap);
					SerializeObject(binaryExpr.Method, writer, serializedObjects, objectMap);
					SerializeExpression(binaryExpr.Conversion, writer, serializedObjects, objectMap);
					break;

				case ExpressionType.ArrayLength:
				case ExpressionType.Convert:
				case ExpressionType.ConvertChecked:
				case ExpressionType.Negate:
				case ExpressionType.NegateChecked:
				case ExpressionType.Not:
				case ExpressionType.Quote:
				case ExpressionType.TypeAs:
				case ExpressionType.UnaryPlus:
					var unaryExpr = (UnaryExpression)expression;
					SerializeExpression(unaryExpr.Operand, writer, serializedObjects, objectMap);
					SerializeObject(unaryExpr.Method, writer, serializedObjects, objectMap);
					break;

				case ExpressionType.Conditional:
					var condExpr = (ConditionalExpression)expression;
					SerializeExpression(condExpr.Test, writer, serializedObjects, objectMap);
					SerializeExpression(condExpr.IfTrue, writer, serializedObjects, objectMap);
					SerializeExpression(condExpr.IfFalse, writer, serializedObjects, objectMap);
					break;

				case ExpressionType.Invoke:
					var invokeExpr = (InvocationExpression)expression;
					SerializeExpression(invokeExpr.Expression, writer, serializedObjects, objectMap);
					writer.Write(invokeExpr.Arguments.Count);
					foreach (var arg in invokeExpr.Arguments)
					{
						SerializeExpression(arg, writer, serializedObjects, objectMap);
					}

					break;

				case ExpressionType.New:
					var newExpr = (NewExpression)expression;
					SerializeObject(newExpr.Constructor, writer, serializedObjects, objectMap);
					writer.Write(newExpr.Arguments.Count);
					foreach (var arg in newExpr.Arguments)
					{
						SerializeExpression(arg, writer, serializedObjects, objectMap);
					}

					if (newExpr.Members != null)
					{
						writer.Write(newExpr.Members.Count);
						foreach (var member in newExpr.Members)
						{
							writer.Write(member.Name);
							writer.Write(member.MemberType.ToString());
						}
					}
					else
					{
						writer.Write(0);
					}

					break;

				case ExpressionType.NewArrayInit:
				case ExpressionType.NewArrayBounds:
					var newArrayExpr = (NewArrayExpression)expression;
					writer.Write(newArrayExpr.Expressions.Count);
					foreach (var expr in newArrayExpr.Expressions)
					{
						SerializeExpression(expr, writer, serializedObjects, objectMap);
					}

					break;

				case ExpressionType.ListInit:
					var listInitExpr = (ListInitExpression)expression;
					SerializeExpression(listInitExpr.NewExpression, writer, serializedObjects, objectMap);
					writer.Write(listInitExpr.Initializers.Count);
					foreach (var init in listInitExpr.Initializers)
					{
						writer.Write(init.AddMethod?.Name ?? string.Empty);
						writer.Write(init.Arguments.Count);
						foreach (var arg in init.Arguments)
						{
							SerializeExpression(arg, writer, serializedObjects, objectMap);
						}
					}

					break;

				case ExpressionType.MemberInit:
					var memberInitExpr = (MemberInitExpression)expression;
					SerializeExpression(memberInitExpr.NewExpression, writer, serializedObjects, objectMap);
					writer.Write(memberInitExpr.Bindings.Count);
					foreach (var binding in memberInitExpr.Bindings)
					{
						writer.Write(binding.Member.Name);
						writer.Write(binding.BindingType.ToString());
						// For simplicity, only handle MemberAssignment
						if (binding is MemberAssignment assign)
						{
							SerializeExpression(assign.Expression, writer, serializedObjects, objectMap);
						}
						else
						{
							SerializeExpression(null, writer, serializedObjects, objectMap); // Placeholder
						}
					}

					break;

				case ExpressionType.TypeIs:
					var typeBinaryExpr = (TypeBinaryExpression)expression;
					SerializeExpression(typeBinaryExpr.Expression, writer, serializedObjects, objectMap);
					WriteTypeInfo(writer, typeBinaryExpr.TypeOperand);
					break;

				default:
					throw new NotSupportedException($"Expression type {expression.NodeType} is not supported.");
			}
		}
		
		private object DeserializeExpression(BinaryReader reader,
			Dictionary<int, object> deserializedObjects)
		{
			var marker = reader.ReadByte();
			if (marker == 0) // Null expression
			{
				return null;
			}

			// Read NodeType
			var nodeType = (ExpressionType)reader.ReadInt32();

			// Read Type
			var exprType = ReadTypeInfo(reader);

			Expression result = null;

			switch (nodeType)
			{
				case ExpressionType.Constant:
					var value = DeserializeObject(reader, deserializedObjects);
					result = Expression.Constant(value, exprType);
					break;

				case ExpressionType.Parameter:
					var paramName = reader.ReadString();
					result = Expression.Parameter(exprType, paramName);
					break;

				case ExpressionType.MemberAccess:
					var maMemberName = reader.ReadString();
					var maMemberTypeStr = reader.ReadString();
					var maMemberType = (MemberTypes)Enum.Parse(typeof(MemberTypes), maMemberTypeStr);
					var maMemberExpr =
						(Expression)DeserializeExpression(reader, deserializedObjects);
					var maMemberInfo = maMemberExpr.Type.GetMember(maMemberName, maMemberType,
							BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
						.FirstOrDefault();
					if (maMemberInfo != null)
					{
						result = Expression.MakeMemberAccess(maMemberExpr, maMemberInfo);
					}

					break;

				case ExpressionType.Call:
					var method = (MethodInfo)DeserializeObject(reader, deserializedObjects);
					var callObject =
						(Expression)DeserializeExpression(reader, deserializedObjects);
					var argCount = reader.ReadInt32();
					var arguments = new Expression[argCount];
					for (int i = 0; i < argCount; i++)
					{
						arguments[i] =
							(Expression)DeserializeExpression(reader, deserializedObjects);
					}

					result = Expression.Call(callObject, method, arguments);
					break;

				case ExpressionType.Lambda:
					var lambdaName = reader.ReadString();
					var tailCall = reader.ReadBoolean();
					var body = (Expression)DeserializeExpression(reader, deserializedObjects);
					var paramCount = reader.ReadInt32();
					var parameters = new ParameterExpression[paramCount];
					for (int i = 0; i < paramCount; i++)
					{
						parameters[i] = (ParameterExpression)DeserializeExpression(reader,
							deserializedObjects);
					}

					// Replace parameters in body with the deserialized parameters
					body = ReplaceParameters(body, parameters);
					result = Expression.Lambda(exprType, body, lambdaName, tailCall, parameters);
					break;

				case ExpressionType.Add:
				case ExpressionType.AddChecked:
				case ExpressionType.And:
				case ExpressionType.AndAlso:
				case ExpressionType.ArrayIndex:
				case ExpressionType.Coalesce:
				case ExpressionType.Divide:
				case ExpressionType.Equal:
				case ExpressionType.ExclusiveOr:
				case ExpressionType.GreaterThan:
				case ExpressionType.GreaterThanOrEqual:
				case ExpressionType.LeftShift:
				case ExpressionType.LessThan:
				case ExpressionType.LessThanOrEqual:
				case ExpressionType.Modulo:
				case ExpressionType.Multiply:
				case ExpressionType.MultiplyChecked:
				case ExpressionType.NotEqual:
				case ExpressionType.Or:
				case ExpressionType.OrElse:
				case ExpressionType.Power:
				case ExpressionType.RightShift:
				case ExpressionType.Subtract:
				case ExpressionType.SubtractChecked:
					var left = (Expression)DeserializeExpression(reader, deserializedObjects);
					var right = (Expression)DeserializeExpression(reader, deserializedObjects);
					var binaryMethod = (MethodInfo)DeserializeObject(reader, deserializedObjects);
					var conversion = (LambdaExpression)DeserializeExpression(reader,
						deserializedObjects);
					result = Expression.MakeBinary(nodeType, left, right, false, binaryMethod, conversion);
					break;

				case ExpressionType.ArrayLength:
				case ExpressionType.Convert:
				case ExpressionType.ConvertChecked:
				case ExpressionType.Negate:
				case ExpressionType.NegateChecked:
				case ExpressionType.Not:
				case ExpressionType.Quote:
				case ExpressionType.TypeAs:
				case ExpressionType.UnaryPlus:
					var operand =
						(Expression)DeserializeExpression(reader, deserializedObjects);
					var unaryMethod = (MethodInfo)DeserializeObject(reader, deserializedObjects);
					result = Expression.MakeUnary(nodeType, operand, exprType, unaryMethod);
					break;

				case ExpressionType.Conditional:
					var test = (Expression)DeserializeExpression(reader, deserializedObjects);
					var ifTrue = (Expression)DeserializeExpression(reader, deserializedObjects);
					var ifFalse =
						(Expression)DeserializeExpression(reader, deserializedObjects);
					result = Expression.Condition(test, ifTrue, ifFalse);
					break;

				case ExpressionType.Invoke:
					var invokeExpr =
						(Expression)DeserializeExpression(reader, deserializedObjects);
					var invokeArgCount = reader.ReadInt32();
					var invokeArgs = new Expression[invokeArgCount];
					for (int i = 0; i < invokeArgCount; i++)
					{
						invokeArgs[i] =
							(Expression)DeserializeExpression(reader, deserializedObjects);
					}

					result = Expression.Invoke(invokeExpr, invokeArgs);
					break;

				case ExpressionType.New:
					var constructor = (ConstructorInfo)DeserializeObject(reader, deserializedObjects);
					var newArgCount = reader.ReadInt32();
					var newArgs = new Expression[newArgCount];
					for (int i = 0; i < newArgCount; i++)
					{
						newArgs[i] =
							(Expression)DeserializeExpression(reader, deserializedObjects);
					}

					var memberCount = reader.ReadInt32();
					var members = new MemberInfo[memberCount];
					for (int i = 0; i < memberCount; i++)
					{
						var newMemberName = reader.ReadString();
						var newMemberTypeStr = reader.ReadString();
						var newMemberTypeEnum = (MemberTypes)Enum.Parse(typeof(MemberTypes), newMemberTypeStr);
						// Simplified: assume instance members
						members[i] = exprType.GetMember(newMemberName, newMemberTypeEnum,
							BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault();
					}

					result = Expression.New(constructor, newArgs, members.Length > 0 ? members : null);
					break;

				case ExpressionType.NewArrayInit:
				case ExpressionType.NewArrayBounds:
					var arrayExprCount = reader.ReadInt32();
					var arrayExprs = new Expression[arrayExprCount];
					for (int i = 0; i < arrayExprCount; i++)
					{
						arrayExprs[i] =
							(Expression)DeserializeExpression(reader, deserializedObjects);
					}

					result = nodeType == ExpressionType.NewArrayInit
						? Expression.NewArrayInit(exprType.GetElementType()!, arrayExprs)
						: Expression.NewArrayBounds(exprType.GetElementType()!, arrayExprs);
					break;

				case ExpressionType.ListInit:
					var listNewExpr =
						(NewExpression)DeserializeExpression(reader, deserializedObjects);
					var initCount = reader.ReadInt32();
					var initializers = new ElementInit[initCount];
					for (int i = 0; i < initCount; i++)
					{
						var liAddMethodName = reader.ReadString();
						var liInitArgCount = reader.ReadInt32();
						var liInitArgs = new Expression[liInitArgCount];
						for (int j = 0; j < liInitArgCount; j++)
						{
							liInitArgs[j] = (Expression)DeserializeExpression(reader,
								deserializedObjects);
						}

						var liAddMethod = exprType.GetMethod(liAddMethodName);
						initializers[i] = Expression.ElementInit(liAddMethod!, liInitArgs);
					}

					result = Expression.ListInit(listNewExpr, initializers);
					break;

				case ExpressionType.MemberInit:
					var memberNewExpr =
						(NewExpression)DeserializeExpression(reader, deserializedObjects);
					var bindingCount = reader.ReadInt32();
					var bindings = new MemberBinding[bindingCount];
					for (int i = 0; i < bindingCount; i++)
					{
						var miBindingMemberName = reader.ReadString();
						reader.ReadString();

						var miBindingExpr =
							(Expression)DeserializeExpression(reader, deserializedObjects);

						var miBindingMember = exprType.GetMember(miBindingMemberName).FirstOrDefault();

						bindings[i] = Expression.Bind(miBindingMember!, miBindingExpr);
					}

					result = Expression.MemberInit(memberNewExpr, bindings);
					break;

				case ExpressionType.TypeIs:
					var typeIsExpr =
						(Expression)DeserializeExpression(reader, deserializedObjects);
					var typeOperand = ReadTypeInfo(reader);
					result = Expression.TypeIs(typeIsExpr, typeOperand);
					break;

				default:
					throw new NotSupportedException($"Expression type {nodeType} is not supported.");
			}

			// Validate the expression
			TypeValidator.ValidateExpression(result);

			return result;
		}

		private Expression ReplaceParameters(Expression expression, ParameterExpression[] newParameters)
		{
			if (newParameters.Length == 0)
				return expression;

			var visitor = new ParameterReplacer(newParameters);
			return visitor.Visit(expression);
		}

		private class ParameterReplacer : ExpressionVisitor
		{
			private readonly Dictionary<string, ParameterExpression> _parameterMap;

			public ParameterReplacer(ParameterExpression[] parameters)
			{
				_parameterMap = parameters.ToDictionary(p => p.Name);
			}

			protected override Expression VisitParameter(ParameterExpression node)
			{
				if (_parameterMap.TryGetValue(node.Name, out var replacement))
				{
					return replacement;
				}

				return base.VisitParameter(node);
			}
		}
	}
}