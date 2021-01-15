using System.Configuration;

namespace CoreRemoting.ClassicRemotingApi.ConfigSection
{
    public class WellKnownServiceConfigElementCollection : ConfigurationElementCollection
    {
        public WellKnownServiceConfigElement this[int index]
        {
            get => (WellKnownServiceConfigElement) BaseGet(index);
            set
            {
                if (BaseGet(index) != null)
                    BaseRemoveAt(index);

                BaseAdd(index, value);
            }
        }

        public new WellKnownServiceConfigElement this[string key]
        {
            get => (WellKnownServiceConfigElement)BaseGet(key);
            set
            {
                if (BaseGet(key) != null)
                    BaseRemoveAt(BaseIndexOf(BaseGet(key)));

                BaseAdd(value);
            }
        }

        protected override ConfigurationElement CreateNewElement()
        {
            return new WellKnownServiceConfigElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((WellKnownServiceConfigElement)element).ServiceName;
        }
    }
}