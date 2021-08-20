using System.Collections.Generic;
using System.Collections.Specialized;
using Unity.Collections;
using UnityEngine;
using UnityEngine.U2D;
using System;
   

namespace UnityEngine.Rendering.Universal
{
    public class ShadowShape2D : IShadowShape2DProvider.ShadowShapes2D
    {
        delegate int     IndexValueGetter<T>(ref T data, int index);
        delegate Vector2 VertexValueGetter<T>(ref T data, int index);
        delegate int     LengthGetter<T>(ref T data);
    
        public struct Edge
        {
            public int v0;
            public int v1;
            public int nextEdgeIndex;

            public Edge(int indexA, int indexB)
            {
                v0 = indexA;
                v1 = indexB;
                nextEdgeIndex = -1;
            }
        }

        public class EdgeComparer : IEqualityComparer<Edge>
        {
            public bool Equals(Edge edge0, Edge edge1)
            {
                return (edge0.v0 == edge1.v0 && edge0.v1 == edge1.v1) || (edge0.v1 == edge1.v0 && edge0.v0 == edge1.v1);
            }

            public int GetHashCode(Edge edge)
            {
                int v0 = edge.v0;
                int v1 = edge.v1;

                if(edge.v1 < edge.v0)
                {
                    v0 = edge.v1;
                    v1 = edge.v0;
                }

                int hashCode = v0 << 15 | v1;
                return hashCode.GetHashCode();
            }
        }


        static public Dictionary<Edge, int> m_EdgeDictionary = new Dictionary<Edge, int>(new EdgeComparer());  // This is done so we don't create garbage allocating and deallocating a dictionary object
        public NativeArray<Vector2> m_ProvidedVertices;
        public NativeArray<Edge>    m_ProvidedEdges;



        public NativeArray<Vector2> m_ContractionDirection;
        public NativeArray<float>   m_ContractionMaximum;
        public NativeArray<Vector2> m_ContractedVertices;
        private float               m_ContractionDistance = -1.0f;

        // This will needs to use NativeArrays        
    

        private void CalculateEdgesFromLines<T>(T indices, IndexValueGetter<T> valueGetter, LengthGetter<T> lengthGetter)
        {
            int numOfIndices = lengthGetter(ref indices) >> 1;  // Each line is 2 indices

            m_ProvidedEdges = new NativeArray<Edge>(numOfIndices, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < numOfIndices; i+=2)
            {
                int index0 = valueGetter(ref indices, i);
                int index1 = valueGetter(ref indices, i+1);
                m_ProvidedEdges[i] = new Edge(index0, index1); ;
            }
        }

        private void CalculateEdgesFromLineStrip<T>(T indices, IndexValueGetter<T> valueGetter, LengthGetter<T> lengthGetter)
        {
            int numOfIndices = lengthGetter(ref indices);
            int lastIndex = valueGetter(ref indices, numOfIndices - 1);

            m_ProvidedEdges = new NativeArray<Edge>(numOfIndices, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < numOfIndices; i++)
            {
                int curIndex = valueGetter(ref indices, i);
                m_ProvidedEdges[i] = new Edge(lastIndex, curIndex); ;
                lastIndex = curIndex;
            }
        }


        private void CalculateEdgesForSimpleLineStrip(int numOfIndices)
        {
            int lastIndex = numOfIndices - 1;
            m_ProvidedEdges = new NativeArray<Edge>(numOfIndices, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < numOfIndices; i++)
            {
                m_ProvidedEdges[i] = new Edge(lastIndex, i); ;
                lastIndex = i;
            }
        }

        private void CalculateEdgesFromTriangles<T>(T indices, IndexValueGetter<T> valueGetter, LengthGetter<T> lengthGetter)
        {
            // Add our edges to an edge list
            m_EdgeDictionary.Clear();
            for(int i=0;i<lengthGetter(ref indices);i+=3)
            {
                int v0Index = valueGetter(ref indices, i);
                int v1Index = valueGetter(ref indices, i + 1);
                int v2Index = valueGetter(ref indices, i + 2);

                Edge edge0 = new Edge(v0Index, v1Index);
                Edge edge1 = new Edge(v1Index, v2Index);
                Edge edge2 = new Edge(v2Index, v0Index);


                // When a contains key comparison is made edges (A, B) and (B, A) are equal (see EdgeComparer)
                if (m_EdgeDictionary.ContainsKey(edge0))
                    m_EdgeDictionary[edge0] = m_EdgeDictionary[edge0] + 1;
                else
                    m_EdgeDictionary.Add(edge0, 1);

                if (m_EdgeDictionary.ContainsKey(edge1))
                    m_EdgeDictionary[edge1] = m_EdgeDictionary[edge1] + 1;
                else
                    m_EdgeDictionary.Add(edge1, 1);

                if (m_EdgeDictionary.ContainsKey(edge2))
                    m_EdgeDictionary[edge2] = m_EdgeDictionary[edge2] + 1;
                else
                    m_EdgeDictionary.Add(edge2, 1);
            }

            // Determine how many elements to allocate
            int outsideEdges = 0;
            foreach (KeyValuePair<Edge, int> keyValuePair in m_EdgeDictionary)
            {
                if (keyValuePair.Value == 1)
                    outsideEdges++;
            }
            m_ProvidedEdges = new NativeArray<Edge>(outsideEdges, Allocator.Persistent);

            // Populate the array
            foreach (KeyValuePair<Edge, int> keyValuePair in m_EdgeDictionary)
            {
                if (keyValuePair.Value == 1)
                {
                    m_ProvidedEdges[keyValuePair.Key.v0] = keyValuePair.Key;
                }
            }
        }

        private void CalculateContractionDirection()
        {
            m_ContractionDirection = new NativeArray<Vector2>(m_ProvidedVertices.Length, Allocator.Persistent);
            m_ContractionMaximum = new NativeArray<float>(m_ProvidedVertices.Length, Allocator.Persistent);

            for (int i = 0; i < m_ProvidedEdges.Length; i++)
            {
                Edge currentEdge = m_ProvidedEdges[i];
                Edge nextEdge = m_ProvidedEdges[m_ProvidedEdges[i].v1];

                int currentVertexIndex = currentEdge.v1;

                Vector3 v0 = m_ProvidedVertices[currentEdge.v0];
                Vector3 v1 = m_ProvidedVertices[currentEdge.v1];
                Vector3 v2 = m_ProvidedVertices[nextEdge.v1];

                Vector3 normal1 = Vector3.Normalize(Vector3.Cross(Vector3.Normalize(v1-v0), Vector3.forward));
                Vector3 normal2 = Vector3.Normalize(Vector3.Cross(Vector3.Normalize(v2 -v1), Vector3.forward));

                m_ContractionDirection[currentVertexIndex] = 0.5f * (normal1 + normal2);
            }
        }

        private void CalculateContractedVertices(float contractionDistance)
        {

            if (m_ContractedVertices.IsCreated)
                m_ContractedVertices.Dispose();

            m_ContractedVertices = new NativeArray<Vector2>(m_ProvidedVertices.Length, Allocator.Persistent);

            for(int i=0;i<m_ProvidedVertices.Length;i++)
            {
                m_ContractedVertices[i] = m_ProvidedVertices[i] + contractionDistance * m_ContractionDirection[i];
            }
        }

        private Vector2 Vector2ArrayGetter(ref Vector2[] array, int index) { return array[index]; }
        private int Vector2ArrayLengthGetter(ref Vector2[] array) { return array.Length; }
        private Vector2 Vector3ArrayGetter(ref Vector3[] array, int index) { return array[index]; }
        private int Vector3ArrayLengthGetter(ref Vector3[] array) { return array.Length; }

        private int UShortArrayGetter(ref ushort[] array, int index) { return array[index]; }
        private int UShortArrayLengthGetter(ref ushort[] array) { return array.Length; }
        private int NativeArrayGetter(ref NativeArray<int> array, int index) { return array[index]; }
        private int NativeArrayLengthGetter(ref NativeArray<int> array) { return array.Length; }

        private void SetEdges<V,I>(V vertices, I indices, VertexValueGetter<V> vertexGetter, LengthGetter<V> vertexLengthGetter, IndexValueGetter<I> indexGetter, LengthGetter<I> indexLengthGetter, IShadowShape2DProvider.OutlineTopology outlineTopology)
        {
            if (m_ProvidedVertices.IsCreated)
                m_ProvidedVertices.Dispose();

            if (m_ProvidedEdges.IsCreated)
                m_ProvidedEdges.Dispose();

            Debug.Assert(vertices != null, "Vertices array cannot be null");
            if (outlineTopology == IShadowShape2DProvider.OutlineTopology.Triangles)
            {
                Debug.Assert(indices != null, "Indices array cannot be null for Triangles topology");

                m_ProvidedVertices = new NativeArray<Vector2>(vertexLengthGetter(ref vertices), Allocator.Persistent);
                CalculateEdgesFromTriangles<I>(indices, indexGetter, indexLengthGetter);
            }
            else if (outlineTopology == IShadowShape2DProvider.OutlineTopology.Lines)
            {

            }
            else if (outlineTopology == IShadowShape2DProvider.OutlineTopology.LineStrip)
            {
                if (indices == null)
                    CalculateEdgesForSimpleLineStrip(vertexLengthGetter(ref vertices));
                else
                    CalculateEdgesFromLineStrip<I>(indices, indexGetter, indexLengthGetter);
            }

            // Copy the vertices
            int numVertices = vertexLengthGetter(ref vertices);
            m_ProvidedVertices = new NativeArray<Vector2>(numVertices, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i=0;i<numVertices;i++)
                m_ProvidedVertices[i] = vertexGetter(ref vertices, i);
        }

        public override void SetEdges(Vector2[] vertices, ushort[] indices, IShadowShape2DProvider.OutlineTopology outlineTopology)
        {
            SetEdges<Vector2[], ushort[]>(vertices, indices, Vector2ArrayGetter, Vector2ArrayLengthGetter, UShortArrayGetter, UShortArrayLengthGetter, outlineTopology);
        }

        public override void SetEdges(Vector3[] vertices, ushort[] indices, IShadowShape2DProvider.OutlineTopology outlineTopology)
        {
            SetEdges<Vector3[], ushort[]>(vertices, indices, Vector3ArrayGetter, Vector3ArrayLengthGetter, UShortArrayGetter, UShortArrayLengthGetter, outlineTopology);
        }

        public override void SetEdges(NativeArray<Vector2> vertices, NativeArray<int> indices, IShadowShape2DProvider.OutlineTopology outlineTopology)
        {
        }

        public void GetEdges(float contractionDistance, out NativeArray<Vector2> vertices, out NativeArray<Edge> edges)
        {
            if (contractionDistance < 0)
                contractionDistance = 0;

            //if (m_ContractionDistance != contractionDistance)
            {
                CalculateContractionDirection();
                CalculateContractedVertices(contractionDistance);

                m_ContractionDistance = contractionDistance;
            }

            vertices = m_ContractedVertices;
            edges = m_ProvidedEdges;
        }

        public override void UpdateEdges(Vector2[] vertices)
        {
            if (m_ProvidedVertices.IsCreated && vertices.Length >= m_ProvidedVertices.Length)
                m_ProvidedVertices.CopyFrom(vertices);
        }

        public override void UpdateEdges(NativeArray<Vector2> vertices)
        {
            if (m_ProvidedVertices.IsCreated && vertices.Length >= m_ProvidedVertices.Length)
                m_ProvidedVertices.CopyFrom(vertices);
           
        }

        ~ShadowShape2D()
        {
            m_ProvidedVertices.Dispose();
            m_ProvidedEdges.Dispose();
            m_ContractionDirection.Dispose();
            m_ContractedVertices.Dispose();
        }
    }
}
