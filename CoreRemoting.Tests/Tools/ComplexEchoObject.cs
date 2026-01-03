using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreRemoting.Tests.Tools;

/// <summary>
/// Komplexes Testobjekt für Integrationstests mit verschachtelten Strukturen
/// </summary>
public class ComplexEchoObject
{
    public string Text { get; set; } = "Test";
    public int Number { get; set; } = 42;
    public double DecimalValue { get; set; } = 3.14159;
    public bool Flag { get; set; } = true;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Guid Identifier { get; set; } = Guid.NewGuid();
    
    public List<string> StringList { get; set; } = new List<string> { "Item1", "Item2", "Item3" };
    public Dictionary<string, int> Dictionary { get; set; } = new Dictionary<string, int>
    {
        { "Key1", 100 },
        { "Key2", 200 },
        { "Key3", 300 }
    };
    
    public NestedObject Nested { get; set; } = new NestedObject();
    public NestedObject[] NestedArray { get; set; } = new[] { new NestedObject(), new NestedObject() };
    
    public enum TestEnum
    {
        First,
        Second,
        Third
    }
    
    public TestEnum EnumValue { get; set; } = TestEnum.Second;
    
    public override bool Equals(object obj)
    {
        if (obj is not ComplexEchoObject other)
            return false;
            
        return Text == other.Text &&
               Number == other.Number &&
               Math.Abs(DecimalValue - other.DecimalValue) < 0.00001 &&
               Flag == other.Flag &&
               Timestamp == other.Timestamp &&
               Identifier == other.Identifier &&
               StringList.SequenceEqual(other.StringList) &&
               Dictionary.SequenceEqual(other.Dictionary) &&
               Nested.Equals(other.Nested) &&
               NestedArray.Length == other.NestedArray.Length &&
               EnumValue == other.EnumValue &&
               NestedArray.Zip(other.NestedArray, (a, b) => a.Equals(b)).All(x => x);
    }
    
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Text);
        hash.Add(Number);
        hash.Add(DecimalValue);
        hash.Add(Flag);
        hash.Add(Timestamp);
        hash.Add(Identifier);
        hash.Add(StringList);
        hash.Add(Dictionary);
        hash.Add(Nested);
        hash.Add(EnumValue);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Verschachteltes Objekt für komplexe Teststrukturen
/// </summary>
public class NestedObject
{
    public string Name { get; set; } = "Nested";
    public int Value { get; set; } = 123;
    public double[] DoubleArray { get; set; } = new[] { 1.1, 2.2, 3.3 };
    
    public override bool Equals(object obj)
    {
        if (obj is not NestedObject other)
            return false;
            
        return Name == other.Name &&
               Value == other.Value &&
               DoubleArray.SequenceEqual(other.DoubleArray);
    }
    
    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Value, DoubleArray);
    }
}