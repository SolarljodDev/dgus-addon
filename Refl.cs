using System;
using System.Reflection;

namespace DgusPlus
{
    // Доступ к приватным полям/методам DGUS по имени.
    static class R
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Static |
                               BindingFlags.Public | BindingFlags.NonPublic;

        public static object Get(object o, string name)
        {
            if (o == null) return null;
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, F | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(o);
                PropertyInfo p = t.GetProperty(name, F | BindingFlags.DeclaredOnly);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(o, null);
            }
            return null;
        }

        public static bool Set(object o, string name, object value)
        {
            if (o == null) return false;
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, F | BindingFlags.DeclaredOnly);
                if (f != null) { f.SetValue(o, value); return true; }
                PropertyInfo p = t.GetProperty(name, F | BindingFlags.DeclaredOnly);
                if (p != null && p.CanWrite) { p.SetValue(o, value, null); return true; }
            }
            return false;
        }

        public static object Call(object o, string name, params object[] args)
        {
            if (o == null) return null;
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                foreach (MethodInfo m in t.GetMethods(F | BindingFlags.DeclaredOnly))
                {
                    if (m.Name == name && m.GetParameters().Length == args.Length)
                        return m.Invoke(o, args);
                }
            }
            throw new MissingMethodException(o.GetType().Name, name);
        }

        public static int GetInt(object o, string name, int dflt)
        {
            try
            {
                object v = Get(o, name);
                return v == null ? dflt : Convert.ToInt32(v);
            }
            catch { return dflt; }
        }
    }
}
