// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;

namespace FhirLoader.Tool.Helpers
{
    public static class EnumHelper
    {
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
