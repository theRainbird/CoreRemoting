using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CoreRemoting.Serialization.NeoBinary;

/// <summary>
/// Diagnostic tool for analyzing and validating NeoBinary streams.
/// Provides detailed analysis of serialization format, corruption detection, and debugging information.
/// </summary>
public static class NeoBinaryStreamAnalyzer
{
	/// <summary>
	/// Analyzes a NeoBinary stream and provides detailed diagnostic information.
	/// </summary>
	/// <param name="stream">Stream to analyze</param>
	/// <returns>Detailed analysis report</returns>
	public static StreamAnalysisReport AnalyzeStream(Stream stream)
	{
		if (stream == null)
			throw new ArgumentNullException(nameof(stream));

		var report = new StreamAnalysisReport();
		var originalPosition = stream.CanSeek ? stream.Position : -1;

		try
		{
			using var reader = new BinaryReader(stream, Encoding.UTF8, true);

			// Analyze header
			AnalyzeHeader(reader, report);

			// Analyze object structure
			AnalyzeObjectStructure(reader, report);

			// Check for common corruption patterns
			DetectCorruptionPatterns(reader, report);

			report.IsValid = !report.HasErrors;
			report.AnalysisCompleted = true;
		}
		catch (Exception ex)
		{
			report.Errors.Add($"Analysis failed: {ex.Message}");
			report.IsValid = false;
		}
		finally
		{
			// Restore original position if possible
			if (originalPosition >= 0 && stream.CanSeek) stream.Position = originalPosition;
		}

		return report;
	}

	/// <summary>
	/// Analyzes the NeoBinary header structure.
	/// </summary>
	private static void AnalyzeHeader(BinaryReader reader, StreamAnalysisReport report)
	{
		if (!reader.BaseStream.CanSeek || reader.BaseStream.Length < 4)
		{
			report.Errors.Add("Stream too short for header analysis");
			return;
		}

		var startPosition = reader.BaseStream.Position;

		// Read magic bytes
		var magicBytes = reader.ReadBytes(4);
		report.MagicBytes = magicBytes;
		report.MagicString = Encoding.ASCII.GetString(magicBytes);

		if (magicBytes.Length != 4 ||
		    magicBytes[0] != (byte)'N' || magicBytes[1] != (byte)'E' ||
		    magicBytes[2] != (byte)'O' || magicBytes[3] != (byte)'B')
		{
			report.Errors.Add($"Invalid magic number: expected 'NEOB', got '{report.MagicString}'");
			report.HasErrors = true;
		}

		// Read version
		if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
		{
			report.Version = reader.ReadUInt16();

			// Read CoreRemoting version
			if (reader.BaseStream.Position < reader.BaseStream.Length) report.CoreRemotingVersion = reader.ReadString();

			// Read flags
			if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
			{
				report.Flags = reader.ReadUInt16();
				report.FlagAnalysis = AnalyzeFlags(report.Flags.Value);
			}
		}
		else
		{
			report.Errors.Add("Stream too short for complete header");
			report.HasErrors = true;
		}

		report.HeaderSize = reader.BaseStream.Position - startPosition;
	}

	/// <summary>
	/// Analyzes the object structure in the stream.
	/// </summary>
	private static void AnalyzeObjectStructure(BinaryReader reader, StreamAnalysisReport report)
	{
		var objectCount = 0;
		var maxDepth = 10; // Prevent infinite recursion
		var currentDepth = 0;

		while (reader.BaseStream.Position < reader.BaseStream.Length && currentDepth < maxDepth)
		{
			var position = reader.BaseStream.Position;

			try
			{
				var marker = reader.ReadByte();
				report.ObjectMarkers.Add((position, marker, GetMarkerDescription(marker)));

				objectCount++;

				// Try to analyze the object based on marker
				AnalyzeObjectAtMarker(reader, marker, report, ref currentDepth);
			}
			catch (Exception ex)
			{
				report.Errors.Add($"Error analyzing object at position {position}: {ex.Message}");
				report.HasErrors = true;
				break;
			}

			// Safety check to prevent infinite loops
			if (objectCount > 1000)
			{
				report.Warnings.Add("Analysis stopped after 1000 objects to prevent infinite loop");
				break;
			}
		}

		report.ObjectCount = objectCount;
	}

	/// <summary>
	/// Analyzes an object at the current stream position based on its marker.
	/// </summary>
	private static void AnalyzeObjectAtMarker(BinaryReader reader, byte marker, StreamAnalysisReport report,
		ref int depth)
	{
		switch (marker)
		{
			case 0: // Null marker
				// Nothing more to read
				break;

			case 1: // Object marker
				AnalyzeComplexObject(reader, report, ref depth);
				break;

			case 2: // Reference marker
				if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
				{
					var objectId = reader.ReadInt32();
					report.ReferenceIds.Add(objectId);
				}

				break;

			case 3: // Simple object marker
				AnalyzeSimpleObject(reader, report);
				break;

			default:
				report.Errors.Add(
					$"Unknown marker: {marker} (0x{marker:X2}) at position {reader.BaseStream.Position - 1}");
				report.HasErrors = true;
				break;
		}
	}

	/// <summary>
	/// Analyzes a complex object structure.
	/// </summary>
	private static void AnalyzeComplexObject(BinaryReader reader, StreamAnalysisReport report, ref int depth)
	{
		if (depth > 10)
		{
			report.Warnings.Add("Maximum analysis depth reached");
			return;
		}

		depth++;

		try
		{
			// Read object ID
			if (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
			{
				var objectId = reader.ReadInt32();
				report.ObjectIds.Add(objectId);
			}

			// Skip type information (complex to parse, just skip it)
			SkipTypeInformation(reader, report);
		}
		finally
		{
			depth--;
		}
	}

	/// <summary>
	/// Analyzes a simple object structure.
	/// </summary>
	private static void AnalyzeSimpleObject(BinaryReader reader, StreamAnalysisReport report)
	{
		// Skip type information
		SkipTypeInformation(reader, report);

		// For simple objects, we can't easily determine the exact size without knowing the type
		// So we'll note this in the report
		report.Warnings.Add("Simple object detected - exact size analysis requires type information");
	}

	/// <summary>
	/// Skips type information during analysis.
	/// </summary>
	private static void SkipTypeInformation(BinaryReader reader, StreamAnalysisReport report)
	{
		try
		{
			// This is a simplified approach - in reality, type information can be complex
			// Read type name, assembly name, and version as strings
			reader.ReadString(); // Type name
			reader.ReadString(); // Assembly name  
			reader.ReadString(); // Version
		}
		catch (Exception ex)
		{
			report.Warnings.Add($"Error skipping type information: {ex.Message}");
		}
	}

	/// <summary>
	/// Detects common corruption patterns in the stream.
	/// </summary>
	private static void DetectCorruptionPatterns(BinaryReader reader, StreamAnalysisReport report)
	{
		if (!reader.BaseStream.CanSeek)
			return;

		var streamLength = reader.BaseStream.Length;
		var suspiciousBytes = new List<(long position, byte value)>();

		// Look for bytes that shouldn't appear as markers
		reader.BaseStream.Position = 0;
		var position = 0L;

		while (position < streamLength)
			try
			{
				var b = reader.ReadByte();
				position++;

				// Check for bytes that are invalid as object markers
				if (b > 3 && b != 0xFE) // Valid markers are 0, 1, 2, 3, and 0xFE
					// Only flag as suspicious if this looks like it could be a marker position
					// This is heuristic and may have false positives
					if (position < streamLength - 4) // Need room for type info at least
						suspiciousBytes.Add((position - 1, b));
			}
			catch
			{
				break;
			}

		if (suspiciousBytes.Count > 0)
		{
			report.Warnings.Add($"Found {suspiciousBytes.Count} potentially invalid marker bytes");
			report.SuspiciousBytes = suspiciousBytes.Take(10).ToList(); // Limit to first 10
		}
	}

	/// <summary>
	/// Analyzes flag bits and their meanings.
	/// </summary>
	private static string AnalyzeFlags(ushort flags)
	{
		var sb = new StringBuilder();

		if ((flags & 0x01) != 0)
			sb.Append("INCLUDE_ASSEMBLY_VERSIONS ");
		if ((flags & 0x02) != 0)
			sb.Append("USE_TYPE_REFERENCES ");
		if ((flags & 0xFC) != 0)
			sb.Append($"RESERVED:{flags & 0xFC} ");

		return sb.ToString().Trim();
	}

	/// <summary>
	/// Gets a human-readable description of a marker.
	/// </summary>
	private static string GetMarkerDescription(byte marker)
	{
		return marker switch
		{
			0 => "NULL_MARKER",
			1 => "OBJECT_MARKER",
			2 => "REFERENCE_MARKER",
			3 => "SIMPLE_OBJECT_MARKER",
			0xFE => "COMPACT_LAYOUT_TAG",
			_ => $"UNKNOWN_MARKER({marker:X2})"
		};
	}

	/// <summary>
	/// Creates a hex dump of the stream for debugging.
	/// </summary>
	public static string CreateHexDump(Stream stream, int maxBytes = 1024)
	{
		if (stream == null)
			throw new ArgumentNullException(nameof(stream));

		var originalPosition = stream.CanSeek ? stream.Position : -1;

		try
		{
			if (stream.CanSeek)
				stream.Position = 0;

			var sb = new StringBuilder();
			var bytesToRead = Math.Min(maxBytes, stream.Length);
			var buffer = new byte[16];
			var offset = 0;

			while (offset < bytesToRead)
			{
				var bytesRead = stream.Read(buffer, 0, Math.Min(16, (int)(bytesToRead - offset)));
				if (bytesRead == 0) break;

				sb.Append($"{offset:X8}: ");

				// Hex bytes
				for (var i = 0; i < 16; i++)
					if (i < bytesRead)
						sb.Append($"{buffer[i]:X2} ");
					else
						sb.Append("   ");

				sb.Append(" |");

				// ASCII representation
				for (var i = 0; i < bytesRead; i++)
					sb.Append(buffer[i] >= 32 && buffer[i] <= 126 ? (char)buffer[i] : '.');

				sb.AppendLine("|");
				offset += bytesRead;
			}

			return sb.ToString();
		}
		finally
		{
			if (originalPosition >= 0 && stream.CanSeek)
				stream.Position = originalPosition;
		}
	}
}

/// <summary>
/// Result of NeoBinary stream analysis.
/// </summary>
public class StreamAnalysisReport
{
	public bool IsValid { get; set; }
	public bool HasErrors { get; set; }
	public bool AnalysisCompleted { get; set; }
	public byte[] MagicBytes { get; set; }
	public string MagicString { get; set; }
	public ushort? Version { get; set; }
	public string CoreRemotingVersion { get; set; }
	public ushort? Flags { get; set; }
	public string FlagAnalysis { get; set; }
	public long HeaderSize { get; set; }
	public int ObjectCount { get; set; }
	public List<(long position, byte marker, string description)> ObjectMarkers { get; set; } = new();
	public List<int> ObjectIds { get; set; } = new();
	public List<int> ReferenceIds { get; set; } = new();
	public List<string> Errors { get; set; } = new();
	public List<string> Warnings { get; set; } = new();
	public List<(long position, byte value)> SuspiciousBytes { get; set; } = new();

	public override string ToString()
	{
		var sb = new StringBuilder();
		sb.AppendLine("=== NeoBinary Stream Analysis Report ===");
		sb.AppendLine($"Valid: {IsValid}");
		sb.AppendLine($"Has Errors: {HasErrors}");
		sb.AppendLine($"Analysis Completed: {AnalysisCompleted}");
		sb.AppendLine();

		sb.AppendLine("Header Information:");
		sb.AppendLine($"  Magic: {MagicString}");
		sb.AppendLine($"  Version: {Version}");
		sb.AppendLine($"  CoreRemoting Version: {CoreRemotingVersion}");
		sb.AppendLine($"  Flags: {Flags} ({FlagAnalysis})");
		sb.AppendLine($"  Header Size: {HeaderSize} bytes");
		sb.AppendLine();

		sb.AppendLine("Content Analysis:");
		sb.AppendLine($"  Object Count: {ObjectCount}");
		sb.AppendLine($"  Object IDs: {ObjectIds.Count}");
		sb.AppendLine($"  Reference IDs: {ReferenceIds.Count}");
		sb.AppendLine();

		if (Errors.Count > 0)
		{
			sb.AppendLine("Errors:");
			foreach (var error in Errors)
				sb.AppendLine($"  - {error}");
			sb.AppendLine();
		}

		if (Warnings.Count > 0)
		{
			sb.AppendLine("Warnings:");
			foreach (var warning in Warnings)
				sb.AppendLine($"  - {warning}");
			sb.AppendLine();
		}

		return sb.ToString();
	}
}