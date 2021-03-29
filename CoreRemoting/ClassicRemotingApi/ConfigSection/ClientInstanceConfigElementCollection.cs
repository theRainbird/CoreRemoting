using System.Configuration;

namespace CoreRemoting.ClassicRemotingApi.ConfigSection
{
    /// <summary>
    /// Collection of ClientInstanceConfigElement objects.
    /// </summary>
    public class ClientInstanceConfigElementCollection : ConfigurationElementCollection
    {
        /// <summary>
        /// Gets an element of the collection by its index.
        /// </summary>
        /// <param name="index">Numeric index (zero based)</param>
        public ClientInstanceConfigElement this[int index]
        {
            get => (ClientInstanceConfigElement) BaseGet(index);
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
        public new ClientInstanceConfigElement this[string key]
        {
            get => (ClientInstanceConfigElement)BaseGet(key);
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
        /// <returns>New ClientInstanceConfigElement object</returns>
        protected override ConfigurationElement CreateNewElement()
        {
            return new ClientInstanceConfigElement();
        }

        /// <summary>
        /// Get the unique key of a specified element.
        /// </summary>
        /// <param name="element">Configuration element</param>
        /// <returns>Unique key</returns>
        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((ClientInstanceConfigElement) element).UniqueInstanceName;
        } 
    }
}