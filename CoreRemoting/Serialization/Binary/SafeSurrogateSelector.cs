/*
 * Code is copied from https://github.com/zyanfx/SafeDeserializationHelpers
 * Many thanks to yallie for this great extensions to make BinaryFormatter a lot safer.
 */

namespace CoreRemoting.Serialization.Binary
{
    using System;
    using System.Data;
    using System.Runtime.Serialization;
    using System.Security.Permissions;
    using System.Security.Principal;

    /// <summary>
    /// Safe surrogate selector provides surrogates for DataSet and WindowsIdentity classes.
    /// </summary>
    internal sealed class SafeSurrogateSelector : ISurrogateSelector
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SafeSurrogateSelector"/> class.
        /// </summary>
        /// <param name="nextSelector">Next <see cref="ISurrogateSelector"/>, optional.</param>
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
        public SafeSurrogateSelector(ISurrogateSelector nextSelector = null)
        {
            if (nextSelector != null)
            {
                SurrogateSelector.ChainSelector(nextSelector);
            }

            // register known surrogates for all streaming contexts
            var ctx = new StreamingContext(StreamingContextStates.All);
            SurrogateSelector.AddSurrogate(typeof(DataSet), ctx, new DataSetSurrogate());
            SurrogateSelector.AddSurrogate(typeof(WindowsIdentity), ctx, new WindowsIdentitySurrogate());
        }

        private SurrogateSelector SurrogateSelector { get; } = new SurrogateSelector();

        /// <inheritdoc cref="ISurrogateSelector" />
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
        public void ChainSelector(ISurrogateSelector selector)
        {
            if (selector != null)
            {
                SurrogateSelector.ChainSelector(selector);
            }
        }

        /// <inheritdoc cref="ISurrogateSelector" />
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
        public ISurrogateSelector GetNextSelector()
        {
            return SurrogateSelector.GetNextSelector();
        }

        /// <inheritdoc cref="ISurrogateSelector" />
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
        public ISerializationSurrogate GetSurrogate(Type type, StreamingContext context, out ISurrogateSelector selector)
        {
            return SurrogateSelector.GetSurrogate(type, context, out selector);
        }
    }
}