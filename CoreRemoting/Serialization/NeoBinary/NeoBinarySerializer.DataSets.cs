using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;

namespace CoreRemoting.Serialization.NeoBinary;

partial class NeoBinarySerializer
{
	private void SerializeDataSet(DataSet dataSet, BinaryWriter writer, HashSet<object> serializedObjects,
		Dictionary<object, int> objectMap)
	{
		if (Config.EnableBinaryDataSetSerialization)
		{
			writer.Write((byte)1); // Binary marker
			SerializeDataSetBinary(dataSet, writer, serializedObjects, objectMap);
		}
		else
		{
			// XML serialization (default)
			writer.Write((byte)0); // XML marker
			var schemaBytes = System.Buffers.ArrayPool<byte>.Shared.Rent(4096);
			try
			{
				using var ms = new MemoryStream(schemaBytes);
				dataSet.WriteXmlSchema(ms);
				var schemaXml = Encoding.UTF8.GetString(schemaBytes, 0, (int)ms.Position);
				writer.Write(schemaXml);

				ms.SetLength(0);
				dataSet.WriteXml(ms, XmlWriteMode.DiffGram);
				var diffGramXml = Encoding.UTF8.GetString(schemaBytes, 0, (int)ms.Position);
				writer.Write(diffGramXml);
			}
			finally
			{
				System.Buffers.ArrayPool<byte>.Shared.Return(schemaBytes);
			}
		}
	}

	private void SerializeDataSetBinary(DataSet dataSet, BinaryWriter writer, HashSet<object> serializedObjects,
		Dictionary<object, int> objectMap)
	{
		// Serialize DataSet properties
		writer.Write(dataSet.DataSetName ?? string.Empty);

		// Calculate flags for non-default properties
		byte flags = 0;
		if (!string.IsNullOrEmpty(dataSet.Namespace)) flags |= 1 << 0; // default ""
		if (!string.IsNullOrEmpty(dataSet.Prefix)) flags |= 1 << 1; // default ""
		if (dataSet.CaseSensitive) flags |= 1 << 2; // default false
		if (dataSet.Locale != null && dataSet.Locale != System.Globalization.CultureInfo.CurrentCulture)
			flags |= 1 << 3; // default CurrentCulture
		if (!dataSet.EnforceConstraints) flags |= 1 << 4; // default true

		writer.Write(flags);

		// Serialize non-default values
		if ((flags & (1 << 0)) != 0) writer.Write(dataSet.Namespace ?? string.Empty);
		if ((flags & (1 << 1)) != 0) writer.Write(dataSet.Prefix ?? string.Empty);
		if ((flags & (1 << 2)) != 0) writer.Write(dataSet.CaseSensitive);
		if ((flags & (1 << 3)) != 0) writer.Write(dataSet.Locale != null ? dataSet.Locale.Name : string.Empty);
		if ((flags & (1 << 4)) != 0) writer.Write(dataSet.EnforceConstraints);

		// Serialize Tables
		writer.Write(dataSet.Tables.Count);
		foreach (DataTable table in dataSet.Tables)
			SerializeDataTableBinary(table, writer, serializedObjects, objectMap);

		// Serialize Relations
		writer.Write(dataSet.Relations.Count);
		foreach (DataRelation relation in dataSet.Relations)
		{
			writer.Write(relation.RelationName ?? string.Empty);
			writer.Write(relation.ParentTable.TableName ?? string.Empty);
			writer.Write(relation.ChildTable.TableName ?? string.Empty);

			// Parent columns
			writer.Write(relation.ParentColumns.Length);
			foreach (var col in relation.ParentColumns) writer.Write(col.ColumnName ?? string.Empty);

			// Child columns
			writer.Write(relation.ChildColumns.Length);
			foreach (var col in relation.ChildColumns) writer.Write(col.ColumnName ?? string.Empty);

			writer.Write(relation.Nested);
		}

		// Serialize ExtendedProperties
		SerializeDictionary(dataSet.ExtendedProperties, writer, serializedObjects, objectMap);
	}

	private void SerializeDataTable(DataTable dataTable, BinaryWriter writer, HashSet<object> serializedObjects,
		Dictionary<object, int> objectMap)
	{
		if (Config.EnableBinaryDataSetSerialization)
		{
			writer.Write((byte)1); // Binary marker
			SerializeDataTableBinary(dataTable, writer, serializedObjects, objectMap);
		}
		else
		{
			// XML serialization (default)
			writer.Write((byte)0); // XML marker
			// Do not add the DataTable to a temporary DataSet, as it may already belong to another DataSet
			// and would throw "DataTable already belongs to another DataSet.". Instead, write schema and data
			// directly from the DataTable itself.
			var schemaBytes = System.Buffers.ArrayPool<byte>.Shared.Rent(4096);
			try
			{
				using var ms = new MemoryStream(schemaBytes);
				dataTable.WriteXmlSchema(ms);
				var schemaXml = Encoding.UTF8.GetString(schemaBytes, 0, (int)ms.Position);
				writer.Write(schemaXml);

				ms.SetLength(0);
				dataTable.WriteXml(ms, XmlWriteMode.DiffGram);
				var diffGramXml = Encoding.UTF8.GetString(schemaBytes, 0, (int)ms.Position);
				writer.Write(diffGramXml);
			}
			finally
			{
				System.Buffers.ArrayPool<byte>.Shared.Return(schemaBytes);
			}
		}
	}

	private void SerializeDataTableBinary(DataTable dataTable, BinaryWriter writer,
		HashSet<object> serializedObjects,
		Dictionary<object, int> objectMap)
	{
		// Serialize DataTable properties
		writer.Write(dataTable.TableName ?? string.Empty);

		// Calculate flags for non-default properties
		byte flags = 0;
		if (!string.IsNullOrEmpty(dataTable.Namespace)) flags |= 1 << 0; // default ""
		if (!string.IsNullOrEmpty(dataTable.Prefix)) flags |= 1 << 1; // default ""
		if (dataTable.CaseSensitive) flags |= 1 << 2; // default false
		if (dataTable.Locale != null && dataTable.Locale != System.Globalization.CultureInfo.CurrentCulture)
			flags |= 1 << 3; // default CurrentCulture

		writer.Write(flags);

		// Serialize non-default values
		if ((flags & (1 << 0)) != 0) writer.Write(dataTable.Namespace ?? string.Empty);
		if ((flags & (1 << 1)) != 0) writer.Write(dataTable.Prefix ?? string.Empty);
		if ((flags & (1 << 2)) != 0) writer.Write(dataTable.CaseSensitive);
		if ((flags & (1 << 3)) != 0) writer.Write(dataTable.Locale != null ? dataTable.Locale.Name : string.Empty);

		// Serialize Columns
		writer.Write(dataTable.Columns.Count);
		foreach (DataColumn column in dataTable.Columns)
		{
			writer.Write(column.ColumnName ?? string.Empty);
			writer.Write(column.DataType.FullName ?? string.Empty);

			// Calculate flags for non-default properties
			byte flags1 = 0;
			byte flags2 = 0;

			if (!column.AllowDBNull) flags1 |= 1 << 0; // default true
			if (column.AutoIncrement) flags1 |= 1 << 1; // default false
			if (column.AutoIncrementSeed != 0) flags1 |= 1 << 2; // default 0
			if (column.AutoIncrementStep != 1) flags1 |= 1 << 3; // default 1
			if (column.Caption != column.ColumnName) flags1 |= 1 << 4; // default ColumnName
			if (column.DefaultValue != null && column.DefaultValue != DBNull.Value)
				flags1 |= 1 << 5; // default null
			if (!string.IsNullOrEmpty(column.Expression)) flags1 |= 1 << 6; // default ""
			if (column.MaxLength != -1) flags1 |= 1 << 7; // default -1

			if (column.ReadOnly) flags2 |= 1 << 0; // default false
			if (column.Unique) flags2 |= 1 << 1; // default false

			writer.Write(flags1);
			writer.Write(flags2);

			// Serialize non-default values
			if ((flags1 & (1 << 0)) != 0) writer.Write(column.AllowDBNull);
			if ((flags1 & (1 << 1)) != 0) writer.Write(column.AutoIncrement);
			if ((flags1 & (1 << 2)) != 0) writer.Write(column.AutoIncrementSeed);
			if ((flags1 & (1 << 3)) != 0) writer.Write(column.AutoIncrementStep);
			if ((flags1 & (1 << 4)) != 0) writer.Write(column.Caption ?? string.Empty);
			if ((flags1 & (1 << 5)) != 0)
				SerializeObject(column.DefaultValue, writer, serializedObjects, objectMap);
			if ((flags1 & (1 << 6)) != 0) writer.Write(column.Expression ?? string.Empty);
			if ((flags1 & (1 << 7)) != 0) writer.Write(column.MaxLength);
			if ((flags2 & (1 << 0)) != 0) writer.Write(column.ReadOnly);
			if ((flags2 & (1 << 1)) != 0) writer.Write(column.Unique);
		}

		// Serialize Rows
		writer.Write(dataTable.Rows.Count);
		foreach (DataRow row in dataTable.Rows)
			for (var i = 0; i < dataTable.Columns.Count; i++)
			{
				var value = row[i];
				writer.Write(value != null);
				if (value != null) SerializeObject(value, writer, serializedObjects, objectMap);
			}

		// Serialize Constraints
		writer.Write(dataTable.Constraints.Count);
		foreach (Constraint constraint in dataTable.Constraints)
			if (constraint is UniqueConstraint unique)
			{
				writer.Write((byte)1); // UniqueConstraint
				writer.Write(unique.ConstraintName ?? string.Empty);
				writer.Write(unique.IsPrimaryKey);
				writer.Write(unique.Columns.Length);
				foreach (var col in unique.Columns) writer.Write(col.ColumnName ?? string.Empty);
			}
			else if (constraint is ForeignKeyConstraint fk)
			{
				writer.Write((byte)2); // ForeignKeyConstraint
				writer.Write(fk.ConstraintName ?? string.Empty);
				writer.Write(fk.AcceptRejectRule.ToString());
				writer.Write(fk.DeleteRule.ToString());
				writer.Write(fk.UpdateRule.ToString());
				writer.Write(fk.RelatedTable?.TableName ?? string.Empty);

				// Columns
				writer.Write(fk.Columns.Length);
				foreach (var col in fk.Columns) writer.Write(col.ColumnName ?? string.Empty);

				writer.Write(fk.RelatedColumns.Length);
				foreach (var col in fk.RelatedColumns) writer.Write(col.ColumnName ?? string.Empty);
			}
			else
			{
				// Unknown constraint type, skip
				writer.Write((byte)0);
			}

		// Serialize ExtendedProperties
		SerializeDictionary(dataTable.ExtendedProperties, writer, serializedObjects, objectMap);
	}

	private object DeserializeDataSet(Type type, BinaryReader reader, Dictionary<int, object> deserializedObjects,
		int objectId)
	{
		var isBinary = reader.ReadByte() == 1;
		if (isBinary)
		{
			return DeserializeDataSetBinary(type, reader, deserializedObjects, objectId);
		}
		else
		{
			// XML deserialization
			var schemaXml = reader.ReadString();
			var diffGramXml = reader.ReadString();
			var dataSet = (DataSet)CreateInstanceWithoutConstructor(type);
			deserializedObjects[objectId] = dataSet;
			using var sr = new StringReader(schemaXml);
			dataSet.ReadXmlSchema(sr);
			using var sr2 = new StringReader(diffGramXml);
			dataSet.ReadXml(sr2, XmlReadMode.DiffGram);
			return dataSet;
		}
	}

	private object DeserializeDataSetBinary(Type type, BinaryReader reader,
		Dictionary<int, object> deserializedObjects,
		int objectId)
	{
		var dataSet = (DataSet)CreateInstanceWithoutConstructor(type);
		deserializedObjects[objectId] = dataSet;

		// Deserialize DataSet properties
		dataSet.DataSetName = reader.ReadString();

		var flags = reader.ReadByte();

		// Deserialize non-default values (defaults are already set by DataSet constructor)
		if ((flags & (1 << 0)) != 0) dataSet.Namespace = reader.ReadString();
		if ((flags & (1 << 1)) != 0) dataSet.Prefix = reader.ReadString();
		if ((flags & (1 << 2)) != 0) dataSet.CaseSensitive = reader.ReadBoolean();
		if ((flags & (1 << 3)) != 0)
		{
			var localeName = reader.ReadString();
			if (!string.IsNullOrEmpty(localeName)) dataSet.Locale = new System.Globalization.CultureInfo(localeName);
		}

		if ((flags & (1 << 4)) != 0) dataSet.EnforceConstraints = reader.ReadBoolean();

		// Deserialize Tables
		var tableCount = reader.ReadInt32();
		for (var i = 0; i < tableCount; i++)
		{
			var table = DeserializeDataTableBinary(typeof(DataTable), reader, deserializedObjects, -1);
			dataSet.Tables.Add((DataTable)table);
		}

		// Deserialize Relations
		var relationCount = reader.ReadInt32();
		for (var i = 0; i < relationCount; i++)
		{
			var relationName = reader.ReadString();
			var parentTableName = reader.ReadString();
			var childTableName = reader.ReadString();

			var parentTable = dataSet.Tables[parentTableName];
			var childTable = dataSet.Tables[childTableName];

			var parentColCount = reader.ReadInt32();
			var parentColumns = new DataColumn[parentColCount];
			for (var j = 0; j < parentColCount; j++)
			{
				var colName = reader.ReadString();
				parentColumns[j] = parentTable.Columns[colName];
			}

			var childColCount = reader.ReadInt32();
			var childColumns = new DataColumn[childColCount];
			for (var j = 0; j < childColCount; j++)
			{
				var colName = reader.ReadString();
				childColumns[j] = childTable.Columns[colName];
			}

			var nested = reader.ReadBoolean();

			var relation = new DataRelation(relationName, parentColumns, childColumns, false);
			relation.Nested = nested;
			dataSet.Relations.Add(relation);
		}

		// Deserialize ExtendedProperties
		var extProps = (System.Collections.IDictionary)DeserializeDictionary(typeof(System.Collections.Hashtable),
			reader, deserializedObjects, -1);
		foreach (System.Collections.DictionaryEntry entry in extProps)
			dataSet.ExtendedProperties[entry.Key] = entry.Value;

		return dataSet;
	}

	private object DeserializeDataTable(Type type, BinaryReader reader, Dictionary<int, object> deserializedObjects,
		int objectId)
	{
		var isBinary = reader.ReadByte() == 1;
		if (isBinary)
		{
			return DeserializeDataTableBinary(type, reader, deserializedObjects, objectId);
		}
		else
		{
			// XML deserialization
			var schemaXml = reader.ReadString();
			var diffGramXml = reader.ReadString();
			var tempDataSet = new DataSet();
			using var sr = new StringReader(schemaXml);
			tempDataSet.ReadXmlSchema(sr);
			using var sr2 = new StringReader(diffGramXml);
			tempDataSet.ReadXml(sr2, XmlReadMode.DiffGram);
			var baseTable = tempDataSet.Tables[0];

			DataTable resultTable;
			if (type == typeof(DataTable))
			{
				resultTable = baseTable;
			}
			else
			{
				// Create typed DataTable instance and merge data from baseTable
				var typedTable = (DataTable)CreateInstanceWithoutConstructor(type);
				typedTable.TableName = baseTable.TableName;
				typedTable.Merge(baseTable, false, MissingSchemaAction.Add);
				resultTable = typedTable;
			}

			deserializedObjects[objectId] = resultTable;
			return resultTable;
		}
	}

	private object DeserializeDataTableBinary(Type type, BinaryReader reader,
		Dictionary<int, object> deserializedObjects,
		int objectId)
	{
		var dataTable = (DataTable)CreateInstanceWithoutConstructor(type);
		if (objectId >= 0) deserializedObjects[objectId] = dataTable;

		// Deserialize DataTable properties
		dataTable.TableName = reader.ReadString();

		var flags = reader.ReadByte();

		// Set defaults
		dataTable.Namespace = string.Empty;
		dataTable.Prefix = string.Empty;
		dataTable.CaseSensitive = false;
		dataTable.Locale = System.Globalization.CultureInfo.CurrentCulture;

		// Deserialize non-default values
		if ((flags & (1 << 0)) != 0) dataTable.Namespace = reader.ReadString();
		if ((flags & (1 << 1)) != 0) dataTable.Prefix = reader.ReadString();
		if ((flags & (1 << 2)) != 0) dataTable.CaseSensitive = reader.ReadBoolean();
		if ((flags & (1 << 3)) != 0)
		{
			var localeName = reader.ReadString();
			if (!string.IsNullOrEmpty(localeName)) dataTable.Locale = new System.Globalization.CultureInfo(localeName);
		}

		// Deserialize Columns
		var columnCount = reader.ReadInt32();
		for (var i = 0; i < columnCount; i++)
		{
			var columnName = reader.ReadString();
			var dataTypeName = reader.ReadString();
			var dataType = Type.GetType(dataTypeName) ?? typeof(string);

			var flags1 = reader.ReadByte();
			var flags2 = reader.ReadByte();

			// Set defaults
			var allowDBNull = true;
			var autoIncrement = false;
			var autoIncrementSeed = 0L;
			var autoIncrementStep = 1L;
			var caption = columnName;
			object defaultValue = null;
			var expression = string.Empty;
			var maxLength = -1;
			var readOnly = false;
			var unique = false;

			// Deserialize non-default values
			if ((flags1 & (1 << 0)) != 0) allowDBNull = reader.ReadBoolean();
			if ((flags1 & (1 << 1)) != 0) autoIncrement = reader.ReadBoolean();
			if ((flags1 & (1 << 2)) != 0) autoIncrementSeed = reader.ReadInt64();
			if ((flags1 & (1 << 3)) != 0) autoIncrementStep = reader.ReadInt64();
			if ((flags1 & (1 << 4)) != 0) caption = reader.ReadString();
			if ((flags1 & (1 << 5)) != 0) defaultValue = DeserializeObject(reader, deserializedObjects);
			if ((flags1 & (1 << 6)) != 0) expression = reader.ReadString();
			if ((flags1 & (1 << 7)) != 0) maxLength = reader.ReadInt32();
			if ((flags2 & (1 << 0)) != 0) readOnly = reader.ReadBoolean();
			if ((flags2 & (1 << 1)) != 0) unique = reader.ReadBoolean();

			var column = new DataColumn(columnName, dataType)
			{
				AllowDBNull = allowDBNull,
				AutoIncrement = autoIncrement,
				AutoIncrementSeed = autoIncrementSeed,
				AutoIncrementStep = autoIncrementStep,
				Caption = caption,
				DefaultValue = defaultValue,
				Expression = expression,
				MaxLength = maxLength,
				ReadOnly = readOnly,
				Unique = unique
			};
			dataTable.Columns.Add(column);
		}

		// Deserialize Rows
		var rowCount = reader.ReadInt32();
		for (var i = 0; i < rowCount; i++)
		{
			var row = dataTable.NewRow();
			for (var j = 0; j < dataTable.Columns.Count; j++)
			{
				var hasValue = reader.ReadBoolean();
				if (hasValue)
				{
					var value = DeserializeObject(reader, deserializedObjects);
					row[j] = value;
				}
				else
				{
					row[j] = DBNull.Value;
				}
			}

			dataTable.Rows.Add(row);
			// Note: RowState is not directly settable; it depends on the operations
		}

		// Deserialize Constraints
		var constraintCount = reader.ReadInt32();
		for (var i = 0; i < constraintCount; i++)
		{
			var constraintType = reader.ReadByte();
			if (constraintType == 1) // UniqueConstraint
			{
				var constraintName = reader.ReadString();
				var isPrimaryKey = reader.ReadBoolean();
				var colCount = reader.ReadInt32();
				var columns = new DataColumn[colCount];
				for (var j = 0; j < colCount; j++)
				{
					var colName = reader.ReadString();
					columns[j] = dataTable.Columns[colName];
				}

				if (!dataTable.Constraints.Contains(constraintName))
				{
					var constraint = new UniqueConstraint(constraintName, columns, isPrimaryKey);
					dataTable.Constraints.Add(constraint);
				}
			}
			else if (constraintType == 2) // ForeignKeyConstraint
			{
				var constraintName = reader.ReadString();
				var acceptRejectRuleStr = reader.ReadString();
				var deleteRuleStr = reader.ReadString();
				var updateRuleStr = reader.ReadString();
				var relatedTableName = reader.ReadString();

				var colCount = reader.ReadInt32();
				var columns = new DataColumn[colCount];
				for (var j = 0; j < colCount; j++)
				{
					var colName = reader.ReadString();
					columns[j] = dataTable.Columns[colName];
				}

				var relatedColCount = reader.ReadInt32();
				var relatedColumns = new DataColumn[relatedColCount];
				for (var j = 0; j < relatedColCount; j++)
				{
					var colName = reader.ReadString();
					// Note: Related table might not be available yet; this is a simplification
					// In a full implementation, we'd need to defer constraint creation
					relatedColumns[j] = null; // Placeholder
				}

				// Skip for now as related table may not be set
				// var constraint = new ForeignKeyConstraint(constraintName, relatedColumns, columns);
				// constraint.AcceptRejectRule = (AcceptRejectRule)Enum.Parse(typeof(AcceptRejectRule), acceptRejectRuleStr);
				// constraint.DeleteRule = (Rule)Enum.Parse(typeof(Rule), deleteRuleStr);
				// constraint.UpdateRule = (Rule)Enum.Parse(typeof(Rule), updateRuleStr);
				// dataTable.Constraints.Add(constraint);
			}
		}

		// Deserialize ExtendedProperties
		var extProps = (System.Collections.IDictionary)DeserializeDictionary(typeof(System.Collections.Hashtable),
			reader, deserializedObjects, -1);

		foreach (System.Collections.DictionaryEntry entry in extProps)
			dataTable.ExtendedProperties[entry.Key] = entry.Value;

		return dataTable;
	}
}