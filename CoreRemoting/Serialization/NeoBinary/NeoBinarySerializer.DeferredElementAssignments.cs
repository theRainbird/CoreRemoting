using System;
using System.Collections.Generic;

namespace CoreRemoting.Serialization.NeoBinary;

partial class NeoBinarySerializer
{
	/// <summary>
	/// Object table of a single deserialization operation.
	/// Besides the map of object IDs to instances it collects the assignments of collection elements
	/// (list items, array elements, dictionary values) that had to be postponed, because the referenced object
	/// was still under construction when the element was read (a cycle running through a collection).
	/// </summary>
	internal sealed class DeserializedObjectTable : Dictionary<int, object>
	{
		/// <summary>
		/// Gets the postponed element assignments together with the ID of the object they are waiting for.
		/// </summary>
		public List<(int ReferencedObjectId, Action<object> AssignReferencedObject)> DeferredElementAssignments
		{
			get;
		} = [];
	}

	/// <summary>
	/// Postpones the assignment of a collection element that refers to an object which is still under construction.
	/// The assignment is executed by <see cref="ResolveDeferredElementAssignments" /> after the object graph is
	/// complete.
	/// </summary>
	/// <param name="deserializedObjects">Object table of the running deserialization operation</param>
	/// <param name="placeholder">Placeholder that was returned instead of the referenced object</param>
	/// <param name="assignReferencedObject">Stores the finished object in the collection</param>
	private static void DeferElementAssignment(Dictionary<int, object> deserializedObjects,
		ForwardReferencePlaceholder placeholder, Action<object> assignReferencedObject)
	{
		if (deserializedObjects is DeserializedObjectTable objectTable)
		{
			objectTable.DeferredElementAssignments.Add((placeholder.ObjectId, assignReferencedObject));
		}
	}

	/// <summary>
	/// Executes all postponed collection element assignments for objects that have been completed meanwhile.
	/// </summary>
	/// <param name="deserializedObjects">Object table of the running deserialization operation</param>
	private static void ResolveDeferredElementAssignments(Dictionary<int, object> deserializedObjects)
	{
		if (deserializedObjects is not DeserializedObjectTable objectTable)
		{
			return;
		}

		foreach (var (referencedObjectId, assignReferencedObject) in objectTable.DeferredElementAssignments)
		{
			if (objectTable.TryGetValue(referencedObjectId, out var referencedObject)
			    && referencedObject is not ForwardReferencePlaceholder)
			{
				assignReferencedObject(referencedObject);
			}
		}

		objectTable.DeferredElementAssignments.Clear();
	}
}
