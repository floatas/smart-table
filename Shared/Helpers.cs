﻿using System;
using System.Linq;
using System.Reflection;

namespace Shared
{
    public static class Helpers
    {
        public static string GetPropertyValues(object obj)
        {
            if (obj == null)
            {
                return null;
            }

            var t = obj.GetType();

            var values = String.Join(",", t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .ToList()
                .Select(x => x.Name + "-" + x.GetValue(obj)));

            return values;
        }
    }
}
