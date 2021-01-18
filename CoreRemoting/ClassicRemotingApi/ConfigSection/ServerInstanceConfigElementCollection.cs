using System.Configuration;

namespace CoreRemoting.ClassicRemotingApi.ConfigSection
{
    /// <summary>
    /// Collection of ServerInstanceConfigElement objects.
    /// </summary>
    public class ServerInstanceConfigElementCollection : ConfigurationElementCollection
    {
        /// <summary>
        /// Gets an element of the collection by its index.
        /// </summary>
        /// <param name="index">Numeric index (zero based)</param>
        public ServerInstanceConfigElement this[int index]
        {
            get => (ServerInstanceConfigElement) BaseGet(index);
            set
            {
                if (BaseGet(index) != null)
                    BaseRemoveAt(index);

                BaseAdd(index, value);
            }
        }

        /// <summary>
        /// Gets an element of the collection by its string key.
        /// </summary>
        /// <param name="key">Unique string key of the element</param>
        public new ServerInstanceConfigElement this[string key]
        {
            get => (ServerInstanceConfigElement)BaseGet(key);
            set
            {
                if (BaseGet(key) != null)
                    BaseRemoveAt(BaseIndexOf(BaseGet(key)));

                BaseAdd(value);
            }
        }

        /// <summary>
        /// Creates a new element.
        /// </summary>
        /// <returns>New ServerInstanceConfigElement object</returns>
        protected override ConfigurationElement CreateNewElement()
        {
            return new ServerInstanceConfigElement();
        }

        /// <summary>
        /// Get the unique key of a specified element.
        /// </summary>
        /// <param name="element">Configuration element</param>
        /// <returns>Unique key</returns>
        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((ServerInstanceConfigElement) element).UniqueInstanceName;
        }
    }
}