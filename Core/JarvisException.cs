using System;

namespace JarvisCSharp.Core
{
    public class JarvisException : Exception
    {
        public JarvisException(string message) : base(message) { }
        public JarvisException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class AIException : JarvisException
    {
        public AIException(string message) : base(message) { }
        public AIException(string message, Exception innerException) : base(message, innerException) { }
    }
}
