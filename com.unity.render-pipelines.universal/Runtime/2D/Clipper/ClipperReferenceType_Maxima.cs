using System;

namespace UnityEngine.Rendering.Universal
{
    using ClipInt = Int64;

    public struct Maxima
    {
        Reference<MaximaStruct> m_Data;

        public void Initialize()
        {
            MaximaStruct initialValue = new MaximaStruct();
            Reference<MaximaStruct>.Create(initialValue, out m_Data);
        }

        public bool IsCreated { get { return m_Data.IsCreated; } }
        public bool IsNull { get { return m_Data.IsNull; } }
        public bool NotNull { get { return !m_Data.IsNull; } }
        public void SetNull() { m_Data.SetNull(); }
        public bool IsEqual(Maxima node) { return m_Data.IsEqual(node.m_Data); }

        //-----------------------------------------------------------------
        //                      Properties
        //-----------------------------------------------------------------
        internal ref ClipInt X { get { return ref m_Data.DeRef().X; } }
        internal ref Maxima Next { get { return ref m_Data.DeRef().Next; } }
        internal ref Maxima Prev { get { return ref m_Data.DeRef().Prev; } }
    }
}
