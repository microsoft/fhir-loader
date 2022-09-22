using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FhirLoader.Common
{
    public static class PackageTypeHelper
    {
        public enum PackageType
        {
            [Description("Conformance")]
            Conformance,
            [Description("fhir.ig")]
            fhirig,
            [Description("Core")]
            Core
        }

        /// <summary>
        /// Check the type of package and return the description..
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="enumValue"></param>
        /// <returns>Description of the passed type.</returns>
        public static string GetDisplayName<T>(object enumValue)
        {
            string displayName = string.Empty;
            if (enumValue != null)
            {
                var memberInfo = typeof(T).GetMember(Enum.GetName(typeof(T), enumValue)!)[0];
                var attribute = memberInfo.GetCustomAttributes(typeof(DescriptionAttribute), false)[0];
                displayName = ((DescriptionAttribute)attribute).Description;
            }
            return displayName;
        }

    }
}
