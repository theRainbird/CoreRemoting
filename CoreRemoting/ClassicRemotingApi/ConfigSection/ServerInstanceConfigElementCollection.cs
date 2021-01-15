using System.Configuration;

namespace CoreRemoting.ClassicRemotingApi.ConfigSection
{
    public class ServerInstanceConfigElementCollection : ConfigurationElementCollection
    {
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

        protected override ConfigurationElement CreateNewElement()
        {
            return new ServerInstanceConfigElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((ServerInstanceConfigElement) element).UniqueInstanceName;
        }
    }
}