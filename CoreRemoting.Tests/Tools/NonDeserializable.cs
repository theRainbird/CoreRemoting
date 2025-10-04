using System;

namespace CoreRemoting.Tests.Tools
{
    [Serializable]
    public class NonDeserializable
    {
        public const string ErrorMessage =
            "This value cannot be deserialized";

        public NonDeserializable(int value) =>
            Value = value;

        public NonDeserializable() =>
            Value = Throw();

        public int Value { get; set; }

        private int Throw() =>
            throw new Exception(ErrorMessage);
    }
}
