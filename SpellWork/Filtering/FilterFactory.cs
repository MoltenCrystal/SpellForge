using SpellWork.Extensions;
using System;
using System.Linq;
using System.Reflection;

namespace SpellWork.Filtering
{
    public enum CompareType
    {
        [FullName("x != y")]
        NotEqual,
        [FullName("x == y")]
        Equal,

        [FullName("x > y")]
        GreaterThan,
        [FullName("x >= y")]
        GreaterOrEqual,
        [FullName("x < y")]
        LowerThan,
        [FullName("x <= y")]
        LowerOrEqual,

        [FullName("x & y == y")]
        AndStrict,
        [FullName("x & y != 0")]
        And,
        [FullName("x & y == 0")]
        NotAnd,

        [FullName("x Starts With y")]
        StartsWith,
        [FullName("x Ends With y")]
        EndsWith,
        [FullName("x Contains y")]
        Contains,
    }

    public static class FilterFactory
    {
        public static Func<T, bool> CreateFilterFunc<T>(MemberInfo field, object val, CompareType compareType)
        {
            Type fieldType = GetMemberType(field);
            switch (Type.GetTypeCode(fieldType))
            {
                case TypeCode.Byte:
                {
                    var filterValue = val.ToUInt32();
                    var getValueFunc = GetGetValueFunc<T, byte>(field);
                    return entry => Compare(getValueFunc(entry), filterValue, compareType);
                }
                case TypeCode.SByte:
                {
                    var filterValue = val.ToInt32();
                    var getValueFunc = GetGetValueFunc<T, sbyte>(field);
                    return entry => Compare(getValueFunc(entry), filterValue, compareType);
                }
                case TypeCode.UInt16:
                {
                    var filterValue = val.ToUInt32();
                    var getValueFunc = GetGetValueFunc<T, ushort>(field);
                    return entry => Compare(getValueFunc(entry), filterValue, compareType);
                }
                case TypeCode.Int16:
                {
                    var filterValue = val.ToInt32();
                    var getValueFunc = GetGetValueFunc<T, short>(field);
                    return entry => Compare(getValueFunc(entry), filterValue, compareType);
                }
                case TypeCode.UInt32:
                {
                    var filterValue = val.ToUInt32();
                    var getValueFunc = GetGetValueFunc<T, uint>(field);
                    return entry => Compare(getValueFunc(entry), filterValue, compareType);
                }
                case TypeCode.Int32:
                {
                    var filterValue = val.ToInt32();
                    var getValueFunc = GetGetValueFunc<T, int>(field);
                    return entry => Compare(getValueFunc(entry), filterValue, compareType);
                }
                case TypeCode.Single:
                {
                    var filterValue = val.ToFloat();
                    var getValueFunc = GetGetValueFunc<T, float>(field);
                    return entry => Compare(getValueFunc(entry), filterValue, compareType);
                }
                case TypeCode.UInt64:
                {
                    var filterValue = val.ToUlong();
                    var getValueFunc = GetGetValueFunc<T, ulong>(field);
                    return entry => Compare(getValueFunc(entry), filterValue, compareType);
                }
                case TypeCode.Int64:
                {
                    var filterValue = val.ToLong();
                    var getValueFunc = GetGetValueFunc<T, long>(field);
                    return entry => Compare(getValueFunc(entry), filterValue, compareType);
                }
                case TypeCode.String:
                {
                    var filterValue = val.ToString();
                    var getValueFunc = GetGetValueFunc<T, string>(field);
                    return entry => Compare(getValueFunc(entry), filterValue, compareType);
                }
                case TypeCode.Object:
                {
                    if (!fieldType.IsArray)
                        break;

                    switch (Type.GetTypeCode(fieldType.GetElementType()))
                    {
                        case TypeCode.Byte:
                        {
                            var filterValue = val.ToUInt32();
                            var getValueFunc = GetGetValueFunc<T, byte[]>(field);
                            return entry => getValueFunc(entry).Any(el => Compare(el, filterValue, compareType));
                        }
                        case TypeCode.SByte:
                        {
                            var filterValue = val.ToInt32();
                            var getValueFunc = GetGetValueFunc<T, sbyte[]>(field);
                            return entry => getValueFunc(entry).Any(el => Compare(el, filterValue, compareType));
                        }
                        case TypeCode.UInt16:
                        {
                            var filterValue = val.ToUInt32();
                            var getValueFunc = GetGetValueFunc<T, ushort[]>(field);
                            return entry => getValueFunc(entry).Any(el => Compare(el, filterValue, compareType));
                        }
                        case TypeCode.Int16:
                        {
                            var filterValue = val.ToInt32();
                            var getValueFunc = GetGetValueFunc<T, short[]>(field);
                            return entry => getValueFunc(entry).Any(el => Compare(el, filterValue, compareType));
                        }
                        case TypeCode.UInt32:
                        {
                            var filterValue = val.ToUInt32();
                            var getValueFunc = GetGetValueFunc<T, uint[]>(field);
                            return entry => getValueFunc(entry).Any(el => Compare(el, filterValue, compareType));
                        }
                        case TypeCode.Int32:
                        {
                            var filterValue = val.ToInt32();
                            var getValueFunc = GetGetValueFunc<T, int[]>(field);
                            return entry => getValueFunc(entry).Any(el => Compare(el, filterValue, compareType));
                        }
                        case TypeCode.Single:
                        {
                            var filterValue = val.ToFloat();
                            var getValueFunc = GetGetValueFunc<T, float[]>(field);
                            return entry => getValueFunc(entry).Any(el => Compare(el, filterValue, compareType));
                        }
                        case TypeCode.UInt64:
                        {
                            var filterValue = val.ToUlong();
                            var getValueFunc = GetGetValueFunc<T, ulong[]>(field);
                            return entry => getValueFunc(entry).Any(el => Compare(el, filterValue, compareType));
                        }
                        case TypeCode.Int64:
                        {
                            var filterValue = val.ToLong();
                            var getValueFunc = GetGetValueFunc<T, long[]>(field);
                            return entry => getValueFunc(entry).Any(el => Compare(el, filterValue, compareType));
                        }
                        case TypeCode.String:
                        {
                            var filterValue = val.ToString();
                            var getValueFunc = GetGetValueFunc<T, string[]>(field);
                            return entry => getValueFunc(entry).Any(el => Compare(el, filterValue, compareType));
                        }
                    }
                    break;
                }
            }

            return entry => false;
        }

        #region Specific Compares

        private static bool Compare(string baseValue, string value, CompareType compareType)
        {
            switch (compareType)
            {
                case CompareType.StartsWith:
                    return baseValue.StartsWith(value);
                case CompareType.EndsWith:
                    return baseValue.EndsWith(value);

                case CompareType.Contains:
                    return baseValue.ContainsText(value);

                case CompareType.NotEqual:
                    return !baseValue.Equals(value, StringComparison.CurrentCultureIgnoreCase);
                default:
                    return baseValue.Equals(value, StringComparison.CurrentCultureIgnoreCase);
            }
        }

        private static bool Compare(float baseValue, float value, CompareType compareType)
        {
            switch (compareType)
            {
                case CompareType.GreaterOrEqual:
                    return baseValue >= value;
                case CompareType.GreaterThan:
                    return baseValue > value;
                case CompareType.LowerOrEqual:
                    return baseValue <= value;
                case CompareType.LowerThan:
                    return baseValue < value;

                case CompareType.NotEqual:
                    return baseValue != value;
                default:
                    return baseValue == value;
            }
        }

        private static bool Compare(ulong baseValue, ulong value, CompareType compareType)
        {
            switch (compareType)
            {
                case CompareType.GreaterOrEqual:
                    return baseValue >= value;
                case CompareType.GreaterThan:
                    return baseValue > value;
                case CompareType.LowerOrEqual:
                    return baseValue <= value;
                case CompareType.LowerThan:
                    return baseValue < value;

                case CompareType.AndStrict:
                    return (baseValue & value) == value;
                case CompareType.And:
                    return (baseValue & value) != 0;
                case CompareType.NotAnd:
                    return (baseValue & value) == 0;

                case CompareType.NotEqual:
                    return baseValue != value;
                default:
                    return baseValue == value;
            }
        }

        private static bool Compare(int baseValue, int value, CompareType compareType)
        {
            switch (compareType)
            {
                case CompareType.GreaterOrEqual:
                    return baseValue >= value;
                case CompareType.GreaterThan:
                    return baseValue > value;
                case CompareType.LowerOrEqual:
                    return baseValue <= value;
                case CompareType.LowerThan:
                    return baseValue < value;

                case CompareType.AndStrict:
                    return (baseValue & value) == value;
                case CompareType.And:
                    return (baseValue & value) != 0;
                case CompareType.NotAnd:
                    return (baseValue & value) == 0;

                case CompareType.NotEqual:
                    return baseValue != value;
                default:
                    return baseValue == value;
            }
        }

        private static bool Compare(uint baseValue, uint value, CompareType compareType)
        {
            switch (compareType)
            {
                case CompareType.GreaterOrEqual:
                    return baseValue >= value;
                case CompareType.GreaterThan:
                    return baseValue > value;
                case CompareType.LowerOrEqual:
                    return baseValue <= value;
                case CompareType.LowerThan:
                    return baseValue < value;

                case CompareType.AndStrict:
                    return (baseValue & value) == value;
                case CompareType.And:
                    return (baseValue & value) != 0;
                case CompareType.NotAnd:
                    return (baseValue & value) == 0;

                case CompareType.NotEqual:
                    return baseValue != value;
                default:
                    return baseValue == value;
            }
        }

        #endregion

        private static Func<T, F> GetGetValueFunc<T, F>(MemberInfo field)
        {
            var fieldInfo = field as FieldInfo;
            if (fieldInfo != null)
                return entry => (F)fieldInfo.GetValue(entry);

            var propertyInfo = field as PropertyInfo;
            if (propertyInfo != null)
            {
                var propertyGetter = propertyInfo.GetGetMethod();
                return (Func<T, F>)Delegate.CreateDelegate(typeof(Func<T, F>), null, propertyGetter);
            }

            return entry => default;
        }

        private static Type GetMemberType(MemberInfo field)
        {
            var fieldInfo = field as FieldInfo;
            if (fieldInfo != null)
                return fieldInfo.FieldType;

            var propertyInfo = field as PropertyInfo;
            return propertyInfo?.PropertyType;
        }
    }
}
