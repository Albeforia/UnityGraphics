/*******************************************************************************
*                                                                              *
* Author    :  Angus Johnson                                                   *
* Version   :  6.4.2                                                           *
* Date      :  27 February 2017                                                *
* Website   :  http://www.angusj.com                                           *
* Copyright :  Angus Johnson 2010-2017                                         *
*                                                                              *
* License:                                                                     *
* Use, modification & distribution is subject to Boost Software License Ver 1. *
* http://www.boost.org/LICENSE_1_0.txt                                         *
*                                                                              *
* Attributions:                                                                *
* The code in this library is an extension of Bala Vatti's clipping algorithm: *
* "A generic solution to polygon clipping"                                     *
* Communications of the ACM, Vol 35, Issue 7 (July 1992) pp 56-63.             *
* http://portal.acm.org/citation.cfm?id=129906                                 *
*                                                                              *
* Computer graphics and geometric modeling: implementation and algorithms      *
* By Max K. Agoston                                                            *
* Springer; 1 edition (January 4, 2005)                                        *
* http://books.google.com/books?q=vatti+clipping+agoston                       *
*                                                                              *
* See also:                                                                    *
* "Polygon Offsetting by Computing Winding Numbers"                            *
* Paper no. DETC2005-85513 pp. 565-575                                         *
* ASME 2005 International Design Engineering Technical Conferences             *
* and Computers and Information in Engineering Conference (IDETC/CIE2005)      *
* September 24-28, 2005 , Long Beach, California, USA                          *
* http://www.me.berkeley.edu/~mcmains/pubs/DAC05OffsetPolygon.pdf              *
*                                                                              *
*******************************************************************************/

/*******************************************************************************
*                                                                              *
* This is a translation of the Delphi Clipper library and the naming style     *
* used has retained a Delphi flavour.                                          *
*                                                                              *
*******************************************************************************/

// Changes introduced. (Venkify).
// * Ability to query Normals along the generated path wrt pivot.
// * Additional information that is helpful if the path needs Tessellation.
// * Above functionality has been tested against single path Offset.
// * Removed unused/unsupported code.

// Additional changes introduced. (Chris Chu).
// * Changed base and derived clipper class into a single struct
// * Split clipper into 3 files.
// * Made Clipper garbage free
//      * Replaced List with UnsafeList
//      * Replaced Classes with RefStrucs
//      * Replaced Null compares with IsNull
//      * Replaced Null assignments with SetNull

// use_lines: Enables open path clipping. Adds a very minor cost to performance.
#define use_lines

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
//using System.Text;          //for Int128.AsString() & StringBuilder
//using System.IO;            //debugging with streamReader & StreamWriter
//using System.Windows.Forms; //debugging to clipboard

namespace UnityEngine.Rendering.Universal
{
    using ClipInt = Int64;
    using Path = UnsafeList<IntPoint>;
    using Paths = UnsafeList<UnsafeList<IntPoint>>;

    internal struct DoublePoint
    {
        public double X;
        public double Y;

        public DoublePoint(double x = 0, double y = 0)
        {
            this.X = x; this.Y = y;
        }

        public DoublePoint(DoublePoint dp)
        {
            this.X = dp.X; this.Y = dp.Y;
        }

        public DoublePoint(IntPoint ip)
        {
            this.X = ip.X; this.Y = ip.Y;
        }
    };


    //------------------------------------------------------------------------------
    // PolyNode classes
    //------------------------------------------------------------------------------

    internal struct PolyNode
    {
        internal bool m_IsCreated;
        internal Path m_polygon;
        internal int m_Index;
        internal JoinType m_jointype;
        internal EndType m_endtype;
        internal UnsafeList<PolyNode> m_Childs;

        public void Initialize()
        {
            m_IsCreated = true;
            m_polygon = new Path(1, Allocator.Temp, NativeArrayOptions.ClearMemory);
            m_Childs = new UnsafeList<PolyNode>(1, Allocator.Temp, NativeArrayOptions.ClearMemory);
        }

        public int ChildCount
        {
            get { return m_Childs.Length; }
        }

        public Path Contour
        {
            get { return m_polygon; }
        }

        internal void AddChild(PolyNode Child)
        {
            int cnt = m_Childs.Length;
            m_Childs.Add(Child);
            Child.m_Index = cnt;
        }

        public PolyNode GetNext()
        {
            if (m_Childs.Length > 0)
                return m_Childs[0];
            else
                return default(PolyNode); // will not be initialized
        }

        public UnsafeList<PolyNode> Childs
        {
            get { return m_Childs; }
        }

        public bool IsOpen { get; set; }
    }


    //------------------------------------------------------------------------------
    // Int128 struct (enables safe math on signed 64bit integers)
    // eg Int128 val1((Int64)9223372036854775807); //ie 2^63 -1
    //    Int128 val2((Int64)9223372036854775807);
    //    Int128 val3 = val1 * val2;
    //    val3.ToString => "85070591730234615847396907784232501249" (8.5e+37)
    //------------------------------------------------------------------------------

    internal struct Int128
    {
        private Int64 hi;
        private UInt64 lo;

        public Int128(Int64 _lo)
        {
            lo = (UInt64)_lo;
            if (_lo < 0) hi = -1;
            else hi = 0;
        }

        public Int128(Int64 _hi, UInt64 _lo)
        {
            lo = _lo;
            hi = _hi;
        }

        public Int128(Int128 val)
        {
            hi = val.hi;
            lo = val.lo;
        }

        public bool IsNegative()
        {
            return hi < 0;
        }

        public static bool operator ==(Int128 val1, Int128 val2)
        {
            return (val1.hi == val2.hi && val1.lo == val2.lo);
        }

        public static bool operator !=(Int128 val1, Int128 val2)
        {
            return !(val1 == val2);
        }

        public override bool Equals(System.Object obj)
        {
            Debug.Assert(false, "This should not be called");

            if (obj == null || !(obj is Int128))
                return false;

            Int128 i128 = (Int128)obj;
            return (i128.hi == hi && i128.lo == lo);
        }

        public override int GetHashCode()
        {
            return hi.GetHashCode() ^ lo.GetHashCode();
        }

        public static bool operator >(Int128 val1, Int128 val2)
        {
            if (val1.hi != val2.hi)
                return val1.hi > val2.hi;
            else
                return val1.lo > val2.lo;
        }

        public static bool operator <(Int128 val1, Int128 val2)
        {
            if (val1.hi != val2.hi)
                return val1.hi < val2.hi;
            else
                return val1.lo < val2.lo;
        }

        public static Int128 operator +(Int128 lhs, Int128 rhs)
        {
            lhs.hi += rhs.hi;
            lhs.lo += rhs.lo;
            if (lhs.lo < rhs.lo) lhs.hi++;
            return lhs;
        }

        public static Int128 operator -(Int128 lhs, Int128 rhs)
        {
            return lhs + -rhs;
        }

        public static Int128 operator -(Int128 val)
        {
            if (val.lo == 0)
                return new Int128(-val.hi, 0);
            else
                return new Int128(~val.hi, ~val.lo + 1);
        }

        public static explicit operator double(Int128 val)
        {
            const double shift64 = 18446744073709551616.0; //2^64
            if (val.hi < 0)
            {
                if (val.lo == 0)
                    return (double)val.hi * shift64;
                else
                    return -(double)(~val.lo + ~val.hi * shift64);
            }
            else
                return (double)(val.lo + val.hi * shift64);
        }

        //nb: Constructing two new Int128 objects every time we want to multiply longs
        //is slow. So, although calling the Int128Mul method doesn't look as clean, the
        //code runs significantly faster than if we'd used the * operator.

        public static Int128 Int128Mul(Int64 lhs, Int64 rhs)
        {
            bool negate = (lhs < 0) != (rhs < 0);
            if (lhs < 0) lhs = -lhs;
            if (rhs < 0) rhs = -rhs;
            UInt64 int1Hi = (UInt64)lhs >> 32;
            UInt64 int1Lo = (UInt64)lhs & 0xFFFFFFFF;
            UInt64 int2Hi = (UInt64)rhs >> 32;
            UInt64 int2Lo = (UInt64)rhs & 0xFFFFFFFF;

            //nb: see comments in clipper.pas
            UInt64 a = int1Hi * int2Hi;
            UInt64 b = int1Lo * int2Lo;
            UInt64 c = int1Hi * int2Lo + int1Lo * int2Hi;

            UInt64 lo;
            Int64 hi;
            hi = (Int64)(a + (c >> 32));

            unchecked { lo = (c << 32) + b; }
            if (lo < b) hi++;
            Int128 result = new Int128(hi, lo);
            return negate ? -result : result;
        }
    };

    //------------------------------------------------------------------------------
    //------------------------------------------------------------------------------

    internal struct IntPoint
    {
        public ClipInt N;
        public ClipInt X;
        public ClipInt Y;
        public ClipInt D;
        public double NX;
        public double NY;

        public IntPoint(ClipInt X, ClipInt Y)
        {
            this.X = X; this.Y = Y;
            this.NX = 0; this.NY = 0;
            this.N = -1; this.D = 0;
        }

        public IntPoint(double x, double y)
        {
            this.X = (ClipInt)x; this.Y = (ClipInt)y;
            this.NX = 0; this.NY = 0;
            this.N = -1; this.D = 0;
        }

        public IntPoint(IntPoint pt)
        {
            this.X = pt.X; this.Y = pt.Y;
            this.NX = pt.NX; this.NY = pt.NY;
            this.N = pt.N; this.D = pt.D;
        }

        public static bool operator ==(IntPoint a, IntPoint b)
        {
            return a.X == b.X && a.Y == b.Y;
        }

        public static bool operator !=(IntPoint a, IntPoint b)
        {
            return a.X != b.X || a.Y != b.Y;
        }

        public override bool Equals(object obj)
        {
            Debug.Assert(false, "This should not be called");
            if (obj == null) return false;
            if (obj is IntPoint)
            {
                IntPoint a = (IntPoint)obj;
                return (X == a.X) && (Y == a.Y);
            }
            else return false;
        }

        public override int GetHashCode()
        {
            //simply prevents a compiler warning
            return base.GetHashCode();
        }
    }// end struct IntPoint

    internal struct IntRect
    {
        public ClipInt left;
        public ClipInt top;
        public ClipInt right;
        public ClipInt bottom;

        public IntRect(ClipInt l, ClipInt t, ClipInt r, ClipInt b)
        {
            this.left = l; this.top = t;
            this.right = r; this.bottom = b;
        }

        public IntRect(IntRect ir)
        {
            this.left = ir.left; this.top = ir.top;
            this.right = ir.right; this.bottom = ir.bottom;
        }
    }

    public enum ClipType { ctIntersection, ctUnion, ctDifference, ctXor };
    public enum PolyType { ptSubject, ptClip };

    //By far the most widely used winding rules for polygon filling are
    //EvenOdd & NonZero (GDI, GDI+, XLib, OpenGL, Cairo, AGG, Quartz, SVG, Gr32)
    //Others rules include Positive, Negative and ABS_GTR_EQ_TWO (only in OpenGL)
    //see http://glprogramming.com/red/chapter11.html
    public enum PolyFillType { pftEvenOdd, pftNonZero, pftPositive, pftNegative };

    public enum JoinType { jtRound };
    public enum EndType { etClosedPolygon, etClosedLine };

    internal enum EdgeSide { esLeft, esRight };
    internal enum Direction { dRightToLeft, dLeftToRight };


    internal struct MyIntersectNodeSort : IComparer<IntersectNode>
    {
        public int Compare(IntersectNode node1, IntersectNode node2)
        {
            ClipInt i = node2.Pt.Y - node1.Pt.Y;
            if (i > 0) return 1;
            else if (i < 0) return -1;
            else return 0;
        }
    }

    internal partial struct Clipper
    {
        //------------------------------------------------------------------------------
        // Constructor
        //------------------------------------------------------------------------------
        public Clipper(int InitOptions = 0) 
        {
            Initialize(out this);   
        }
        public void Initialize()
        {
            Initialize(out this);
        }

        //------------------------------------------------------------------------------

        private void InsertMaxima(ClipInt X)
        {
            //double-linked list: sorted ascending, ignoring dups.
            Maxima newMax = new Maxima();
            newMax.Initialize();
            newMax.X = X;
            if (m_Maxima.IsNull)
            {
                m_Maxima = newMax;
                m_Maxima.Next.SetNull();
                m_Maxima.Prev.SetNull();
            }
            else if (X < m_Maxima.X)
            {
                newMax.Next = m_Maxima;
                newMax.Prev.SetNull();
                m_Maxima = newMax;
            }
            else
            {
                Maxima m = m_Maxima;
                while (m.Next.NotNull && (X >= m.Next.X)) m = m.Next;
                if (X == m.X) return; //ie ignores duplicates (& CG to clean up newMax)
                //insert newMax between m and m.Next ...
                newMax.Next = m.Next;
                newMax.Prev = m;
                if (m.Next.NotNull) m.Next.Prev = newMax;
                m.Next = newMax;
            }
        }

        //------------------------------------------------------------------------------

        public bool Execute(ClipType clipType, ref Paths solution,
            PolyFillType FillType = PolyFillType.pftEvenOdd)
        {
            return Execute(clipType, ref solution, FillType, FillType);
        }

        //------------------------------------------------------------------------------

        public bool Execute(ClipType clipType, ref Paths solution,
            PolyFillType subjFillType, PolyFillType clipFillType)
        {
            if (m_ExecuteLocked) return false;
            if (m_HasOpenPaths)
                throw
                    new ClipperException("Error: PolyTree struct is needed for open path clipping.");

            m_ExecuteLocked = true;
            solution.Clear();
            m_SubjFillType = subjFillType;
            m_ClipFillType = clipFillType;
            m_ClipType = clipType;
            m_UsingPolyTree = false;
            bool succeeded;
            try
            {
                succeeded = ExecuteInternal();
                //build the return polygons ...
                if (succeeded) BuildResult(ref solution);
            }
            finally
            {
                DisposeAllPolyPts();
                m_ExecuteLocked = false;
            }
            return succeeded;
        }

        //------------------------------------------------------------------------------

        private bool ExecuteInternal()
        {
            try
            {
                Reset();
                m_SortedEdges.SetNull();
                m_Maxima.SetNull();

                ClipInt botY, topY;
                if (!PopScanbeam(out botY)) return false;
                InsertLocalMinimaIntoAEL(ref botY);
                while (PopScanbeam(out topY) || LocalMinimaPending())
                {
                    ProcessHorizontals();
                    m_GhostJoins.Clear();
                    if (!ProcessIntersections(topY)) return false;
                    ProcessEdgesAtTopOfScanbeam(topY);
                    botY = topY;
                    InsertLocalMinimaIntoAEL(ref botY);
                }

                //fix orientations ...
                for(int i=0;i<m_PolyOuts.Length;i++)
                {
                    ref OutRec outRec = ref m_PolyOuts.GetIndexByRef(i);

                    if (outRec.Pts.IsNull || outRec.IsOpen) continue;
                    if ((outRec.IsHole ^ ReverseSolution) == (Area(ref outRec) > 0))
                        ReversePolyPtLinks(ref outRec.Pts);
                }

                JoinCommonEdges();

                for(int i=0;i<m_PolyOuts.Length;i++)
                {
                    ref OutRec outRec = ref m_PolyOuts.GetIndexByRef(i);

                    if (outRec.Pts.IsNull)
                        continue;
                    else if (outRec.IsOpen)
                        FixupOutPolyline(ref outRec);
                    else
                        FixupOutPolygon(ref outRec);
                }

                if (StrictlySimple) DoSimplePolygons();
                return true;
            }
            //catch { return false; }
            finally
            {
                m_Joins.Clear();
                m_GhostJoins.Clear();
            }
        }

        //------------------------------------------------------------------------------

        private void DisposeAllPolyPts()
        {
            for (int i = 0; i < m_PolyOuts.Length; ++i) DisposeOutRec(i);
            m_PolyOuts.Clear();
        }

        //------------------------------------------------------------------------------

        private void AddJoin(ref OutPt Op1, ref OutPt Op2, IntPoint OffPt)
        {
            Join j = new Join();
            j.Initialize();
            j.OutPt1 = Op1;
            j.OutPt2 = Op2;
            j.OffPt = OffPt;
            m_Joins.Add(j);
        }

        //------------------------------------------------------------------------------

        private void AddGhostJoin(ref OutPt Op, IntPoint OffPt)
        {
            Join j = new Join();
            j.Initialize();
            j.OutPt1 = Op;
            j.OffPt = OffPt;
            m_GhostJoins.Add(j);
        }

        private void InsertLocalMinimaIntoAEL(ref ClipInt botY)
        {
            LocalMinima lm;
            while (PopLocalMinima(ref botY, out lm))
            {
                TEdge lb = lm.LeftBound;
                TEdge rb = lm.RightBound;

                OutPt Op1 = new OutPt();  // This will be null by default
                if (lb.IsNull)
                {
                    InsertEdgeIntoAEL(ref rb, ref NULL_TEdge);
                    SetWindingCount(ref rb);
                    if (IsContributing(ref rb))
                        Op1 = AddOutPt(ref rb, rb.Bot);
                }
                else if (rb.IsNull)
                {
                    InsertEdgeIntoAEL(ref lb, ref NULL_TEdge);
                    SetWindingCount(ref lb);
                    if (IsContributing(ref lb))
                        Op1 = AddOutPt(ref lb, lb.Bot);
                    InsertScanbeam(ref lb.Top.Y);
                }
                else
                {
                    InsertEdgeIntoAEL(ref lb, ref NULL_TEdge);
                    InsertEdgeIntoAEL(ref rb, ref lb);
                    SetWindingCount(ref lb);
                    rb.WindCnt = lb.WindCnt;
                    rb.WindCnt2 = lb.WindCnt2;
                    if (IsContributing(ref lb))
                        Op1 = AddLocalMinPoly(ref lb, ref rb, lb.Bot);
                    InsertScanbeam(ref lb.Top.Y);
                }

                if (rb.NotNull)
                {
                    if (IsHorizontal(ref rb))
                    {
                        if (rb.NextInLML.NotNull)
                            InsertScanbeam(ref rb.NextInLML.Top.Y);
                        AddEdgeToSEL(ref rb);
                    }
                    else
                        InsertScanbeam(ref rb.Top.Y);
                }

                if (lb.IsNull || rb.IsNull) continue;

                //if output polygons share an Edge with a horizontal rb, they'll need joining later ...
                if (Op1.NotNull && IsHorizontal(ref rb) &&
                    m_GhostJoins.Length > 0 && rb.WindDelta != 0)
                {
                    for (int i = 0; i < m_GhostJoins.Length; i++)
                    {
                        //if the horizontal Rb and a 'ghost' horizontal overlap, then convert
                        //the 'ghost' join to a real join ready for later ...
                        Join j = m_GhostJoins[i];
                        if (HorzSegmentsOverlap(j.OutPt1.Pt.X, j.OffPt.X, rb.Bot.X, rb.Top.X))
                            AddJoin(ref j.OutPt1, ref Op1, j.OffPt);
                    }
                }

                if (lb.OutIdx >= 0 && lb.PrevInAEL.NotNull &&
                    lb.PrevInAEL.Curr.X == lb.Bot.X &&
                    lb.PrevInAEL.OutIdx >= 0 &&
                    SlopesEqual(lb.PrevInAEL.Curr, lb.PrevInAEL.Top, lb.Curr, lb.Top, m_UseFullRange) &&
                    lb.WindDelta != 0 && lb.PrevInAEL.WindDelta != 0)
                {
                    OutPt Op2 = AddOutPt(ref lb.PrevInAEL, lb.Bot);
                    AddJoin(ref Op1, ref Op2, lb.Top);
                }

                if (lb.NextInAEL != rb)
                {
                    if (rb.OutIdx >= 0 && rb.PrevInAEL.OutIdx >= 0 &&
                        SlopesEqual(rb.PrevInAEL.Curr, rb.PrevInAEL.Top, rb.Curr, rb.Top, m_UseFullRange) &&
                        rb.WindDelta != 0 && rb.PrevInAEL.WindDelta != 0)
                    {
                        OutPt Op2 = AddOutPt(ref rb.PrevInAEL, rb.Bot);
                        AddJoin(ref Op1, ref Op2, rb.Top);
                    }

                    TEdge e = lb.NextInAEL;
                    if (e.NotNull)
                        while (e != rb)
                        {
                            //nb: For calculating winding counts etc, IntersectEdges() assumes
                            //that param1 will be to the right of param2 ABOVE the intersection ...
                            IntersectEdges(ref rb, ref e, lb.Curr); //order important here
                            e = e.NextInAEL;
                        }
                }
            }
        }

        //------------------------------------------------------------------------------

        private void InsertEdgeIntoAEL(ref TEdge edge, ref TEdge startEdge)
        {
            if (m_ActiveEdges.IsNull)
            {
                edge.PrevInAEL.SetNull();
                edge.NextInAEL.SetNull();
                m_ActiveEdges = edge;
            }
            else if (startEdge.IsNull && E2InsertsBeforeE1(ref m_ActiveEdges, ref edge))
            {
                edge.PrevInAEL.SetNull();
                edge.NextInAEL = m_ActiveEdges;
                m_ActiveEdges.PrevInAEL = edge;
                m_ActiveEdges = edge;
            }
            else
            {
                if (startEdge.IsNull) startEdge = m_ActiveEdges;
                while (startEdge.NextInAEL.NotNull &&
                       !E2InsertsBeforeE1(ref startEdge.NextInAEL, ref edge))
                    startEdge = startEdge.NextInAEL;
                edge.NextInAEL = startEdge.NextInAEL;
                if (startEdge.NextInAEL.NotNull) startEdge.NextInAEL.PrevInAEL = edge;
                edge.PrevInAEL = startEdge;
                startEdge.NextInAEL = edge;
            }
        }

        //----------------------------------------------------------------------

        private bool E2InsertsBeforeE1(ref TEdge e1, ref TEdge e2)
        {
            if (e2.Curr.X == e1.Curr.X)
            {
                if (e2.Top.Y > e1.Top.Y)
                    return e2.Top.X < TopX(ref e1, e2.Top.Y);
                else return e1.Top.X > TopX(ref e2, e1.Top.Y);
            }
            else return e2.Curr.X < e1.Curr.X;
        }

        //------------------------------------------------------------------------------

        private bool IsEvenOddFillType(ref TEdge edge)
        {
            if (edge.PolyTyp == PolyType.ptSubject)
                return m_SubjFillType == PolyFillType.pftEvenOdd;
            else
                return m_ClipFillType == PolyFillType.pftEvenOdd;
        }

        //------------------------------------------------------------------------------

        private bool IsEvenOddAltFillType(ref TEdge edge)
        {
            if (edge.PolyTyp == PolyType.ptSubject)
                return m_ClipFillType == PolyFillType.pftEvenOdd;
            else
                return m_SubjFillType == PolyFillType.pftEvenOdd;
        }

        //------------------------------------------------------------------------------

        private bool IsContributing(ref TEdge edge)
        {
            PolyFillType pft, pft2;
            if (edge.PolyTyp == PolyType.ptSubject)
            {
                pft = m_SubjFillType;
                pft2 = m_ClipFillType;
            }
            else
            {
                pft = m_ClipFillType;
                pft2 = m_SubjFillType;
            }

            switch (pft)
            {
                case PolyFillType.pftEvenOdd:
                    //return false if a subj line has been flagged as inside a subj polygon
                    if (edge.WindDelta == 0 && edge.WindCnt != 1) return false;
                    break;
                case PolyFillType.pftNonZero:
                    if (Math.Abs(edge.WindCnt) != 1) return false;
                    break;
                case PolyFillType.pftPositive:
                    if (edge.WindCnt != 1) return false;
                    break;
                default: //PolyFillType.pftNegative
                    if (edge.WindCnt != -1) return false;
                    break;
            }

            switch (m_ClipType)
            {
                case ClipType.ctIntersection:
                    switch (pft2)
                    {
                        case PolyFillType.pftEvenOdd:
                        case PolyFillType.pftNonZero:
                            return (edge.WindCnt2 != 0);
                        case PolyFillType.pftPositive:
                            return (edge.WindCnt2 > 0);
                        default:
                            return (edge.WindCnt2 < 0);
                    }
                case ClipType.ctUnion:
                    switch (pft2)
                    {
                        case PolyFillType.pftEvenOdd:
                        case PolyFillType.pftNonZero:
                            return (edge.WindCnt2 == 0);
                        case PolyFillType.pftPositive:
                            return (edge.WindCnt2 <= 0);
                        default:
                            return (edge.WindCnt2 >= 0);
                    }
                case ClipType.ctDifference:
                    if (edge.PolyTyp == PolyType.ptSubject)
                        switch (pft2)
                        {
                            case PolyFillType.pftEvenOdd:
                            case PolyFillType.pftNonZero:
                                return (edge.WindCnt2 == 0);
                            case PolyFillType.pftPositive:
                                return (edge.WindCnt2 <= 0);
                            default:
                                return (edge.WindCnt2 >= 0);
                        }
                    else
                        switch (pft2)
                        {
                            case PolyFillType.pftEvenOdd:
                            case PolyFillType.pftNonZero:
                                return (edge.WindCnt2 != 0);
                            case PolyFillType.pftPositive:
                                return (edge.WindCnt2 > 0);
                            default:
                                return (edge.WindCnt2 < 0);
                        }
                case ClipType.ctXor:
                    if (edge.WindDelta == 0) //XOr always contributing unless open
                        switch (pft2)
                        {
                            case PolyFillType.pftEvenOdd:
                            case PolyFillType.pftNonZero:
                                return (edge.WindCnt2 == 0);
                            case PolyFillType.pftPositive:
                                return (edge.WindCnt2 <= 0);
                            default:
                                return (edge.WindCnt2 >= 0);
                        }
                    else
                        return true;
            }
            return true;
        }

        //------------------------------------------------------------------------------

        private void SetWindingCount(ref TEdge edge)
        {
            TEdge e = edge.PrevInAEL;
            //find the edge of the same polytype that immediately preceeds 'edge' in AEL
            while (e.NotNull && ((e.PolyTyp != edge.PolyTyp) || (e.WindDelta == 0))) e = e.PrevInAEL;
            if (e.IsNull)
            {
                PolyFillType pft;
                pft = (edge.PolyTyp == PolyType.ptSubject ? m_SubjFillType : m_ClipFillType);
                if (edge.WindDelta == 0) edge.WindCnt = (pft == PolyFillType.pftNegative ? -1 : 1);
                else edge.WindCnt = edge.WindDelta;
                edge.WindCnt2 = 0;
                e = m_ActiveEdges; //ie get ready to calc WindCnt2
            }
            else if (edge.WindDelta == 0 && m_ClipType != ClipType.ctUnion)
            {
                edge.WindCnt = 1;
                edge.WindCnt2 = e.WindCnt2;
                e = e.NextInAEL; //ie get ready to calc WindCnt2
            }
            else if (IsEvenOddFillType(ref edge))
            {
                //EvenOdd filling ...
                if (edge.WindDelta == 0)
                {
                    //are we inside a subj polygon ...
                    bool Inside = true;
                    TEdge e2 = e.PrevInAEL;
                    while (e2.NotNull)
                    {
                        if (e2.PolyTyp == e.PolyTyp && e2.WindDelta != 0)
                            Inside = !Inside;
                        e2 = e2.PrevInAEL;
                    }
                    edge.WindCnt = (Inside ? 0 : 1);
                }
                else
                {
                    edge.WindCnt = edge.WindDelta;
                }
                edge.WindCnt2 = e.WindCnt2;
                e = e.NextInAEL; //ie get ready to calc WindCnt2
            }
            else
            {
                //nonZero, Positive or Negative filling ...
                if (e.WindCnt * e.WindDelta < 0)
                {
                    //prev edge is 'decreasing' WindCount (WC) toward zero
                    //so we're outside the previous polygon ...
                    if (Math.Abs(e.WindCnt) > 1)
                    {
                        //outside prev poly but still inside another.
                        //when reversing direction of prev poly use the same WC
                        if (e.WindDelta * edge.WindDelta < 0) edge.WindCnt = e.WindCnt;
                        //otherwise continue to 'decrease' WC ...
                        else edge.WindCnt = e.WindCnt + edge.WindDelta;
                    }
                    else
                        //now outside all polys of same polytype so set own WC ...
                        edge.WindCnt = (edge.WindDelta == 0 ? 1 : edge.WindDelta);
                }
                else
                {
                    //prev edge is 'increasing' WindCount (WC) away from zero
                    //so we're inside the previous polygon ...
                    if (edge.WindDelta == 0)
                        edge.WindCnt = (e.WindCnt < 0 ? e.WindCnt - 1 : e.WindCnt + 1);
                    //if wind direction is reversing prev then use same WC
                    else if (e.WindDelta * edge.WindDelta < 0)
                        edge.WindCnt = e.WindCnt;
                    //otherwise add to WC ...
                    else edge.WindCnt = e.WindCnt + edge.WindDelta;
                }
                edge.WindCnt2 = e.WindCnt2;
                e = e.NextInAEL; //ie get ready to calc WindCnt2
            }

            //update WindCnt2 ...
            if (IsEvenOddAltFillType(ref edge))
            {
                //EvenOdd filling ...
                while (e != edge)
                {
                    if (e.WindDelta != 0)
                        edge.WindCnt2 = (edge.WindCnt2 == 0 ? 1 : 0);
                    e = e.NextInAEL;
                }
            }
            else
            {
                //nonZero, Positive or Negative filling ...
                while (e != edge)
                {
                    edge.WindCnt2 += e.WindDelta;
                    e = e.NextInAEL;
                }
            }
        }

        //------------------------------------------------------------------------------

        private void AddEdgeToSEL(ref TEdge edge)
        {
            //SEL pointers in PEdge are use to build transient lists of horizontal edges.
            //However, since we don't need to worry about processing order, all additions
            //are made to the front of the list ...
            if (m_SortedEdges.IsNull)
            {
                m_SortedEdges = edge;
                edge.PrevInSEL.SetNull();
                edge.NextInSEL.SetNull();
            }
            else
            {
                edge.NextInSEL = m_SortedEdges;
                edge.PrevInSEL.SetNull();
                m_SortedEdges.PrevInSEL = edge;
                m_SortedEdges = edge;
            }
        }

        //------------------------------------------------------------------------------

        internal Boolean PopEdgeFromSEL(out TEdge e)
        {
            //Pop edge from front of SEL (ie SEL is a FILO list)
            e = m_SortedEdges;
            if (e.IsNull) return false;
            TEdge oldE = e;
            m_SortedEdges = e.NextInSEL;
            if (m_SortedEdges.NotNull) m_SortedEdges.PrevInSEL.SetNull();
            oldE.NextInSEL.SetNull();
            oldE.PrevInSEL.SetNull();
            return true;
        }

        //------------------------------------------------------------------------------

        private void CopyAELToSEL()
        {
            TEdge e = m_ActiveEdges;
            m_SortedEdges = e;
            while (e.NotNull)
            {
                e.PrevInSEL = e.PrevInAEL;
                e.NextInSEL = e.NextInAEL;
                e = e.NextInAEL;
            }
        }

        //------------------------------------------------------------------------------

        private void SwapPositionsInSEL(ref TEdge edge1, ref TEdge edge2)
        {
            if (edge1.NextInSEL.IsNull && edge1.PrevInSEL.IsNull)
                return;
            if (edge2.NextInSEL.IsNull && edge2.PrevInSEL.IsNull)
                return;

            if (edge1.NextInSEL == edge2)
            {
                TEdge next = edge2.NextInSEL;
                if (next.NotNull)
                    next.PrevInSEL = edge1;
                TEdge prev = edge1.PrevInSEL;
                if (prev.NotNull)
                    prev.NextInSEL = edge2;
                edge2.PrevInSEL = prev;
                edge2.NextInSEL = edge1;
                edge1.PrevInSEL = edge2;
                edge1.NextInSEL = next;
            }
            else if (edge2.NextInSEL == edge1)
            {
                TEdge next = edge1.NextInSEL;
                if (next.NotNull)
                    next.PrevInSEL = edge2;
                TEdge prev = edge2.PrevInSEL;
                if (prev.NotNull)
                    prev.NextInSEL = edge1;
                edge1.PrevInSEL = prev;
                edge1.NextInSEL = edge2;
                edge2.PrevInSEL = edge1;
                edge2.NextInSEL = next;
            }
            else
            {
                TEdge next = edge1.NextInSEL;
                TEdge prev = edge1.PrevInSEL;
                edge1.NextInSEL = edge2.NextInSEL;
                if (edge1.NextInSEL.NotNull)
                    edge1.NextInSEL.PrevInSEL = edge1;
                edge1.PrevInSEL = edge2.PrevInSEL;
                if (edge1.PrevInSEL.NotNull)
                    edge1.PrevInSEL.NextInSEL = edge1;
                edge2.NextInSEL = next;
                if (edge2.NextInSEL.NotNull)
                    edge2.NextInSEL.PrevInSEL = edge2;
                edge2.PrevInSEL = prev;
                if (edge2.PrevInSEL.NotNull)
                    edge2.PrevInSEL.NextInSEL = edge2;
            }

            if (edge1.PrevInSEL.IsNull)
                m_SortedEdges = edge1;
            else if (edge2.PrevInSEL.IsNull)
                m_SortedEdges = edge2;
        }

        //------------------------------------------------------------------------------


        private void AddLocalMaxPoly(ref TEdge e1, ref TEdge e2, IntPoint pt)
        {
            AddOutPt(ref e1, pt);
            if (e2.WindDelta == 0) AddOutPt(ref e2, pt);
            if (e1.OutIdx == e2.OutIdx)
            {
                e1.OutIdx = Unassigned;
                e2.OutIdx = Unassigned;
            }
            else if (e1.OutIdx < e2.OutIdx)
                AppendPolygon(ref e1, ref e2);
            else
                AppendPolygon(ref e2, ref e1);
        }

        //------------------------------------------------------------------------------

        private OutPt AddLocalMinPoly(ref TEdge e1, ref TEdge e2, IntPoint pt)
        {
            OutPt result;
            TEdge e, prevE;
            if (IsHorizontal(ref e2) || (e1.Dx > e2.Dx))
            {
                result = AddOutPt(ref e1, pt);
                e2.OutIdx = e1.OutIdx;
                e1.Side = EdgeSide.esLeft;
                e2.Side = EdgeSide.esRight;
                e = e1;
                if (e.PrevInAEL == e2)
                    prevE = e2.PrevInAEL;
                else
                    prevE = e.PrevInAEL;
            }
            else
            {
                result = AddOutPt(ref e2, pt);
                e1.OutIdx = e2.OutIdx;
                e1.Side = EdgeSide.esRight;
                e2.Side = EdgeSide.esLeft;
                e = e2;
                if (e.PrevInAEL == e1)
                    prevE = e1.PrevInAEL;
                else
                    prevE = e.PrevInAEL;
            }

            if (prevE.NotNull && prevE.OutIdx >= 0 && prevE.Top.Y < pt.Y && e.Top.Y < pt.Y)
            {
                ClipInt xPrev = TopX(ref prevE, pt.Y);
                ClipInt xE = TopX(ref e, pt.Y);
                if ((xPrev == xE) && (e.WindDelta != 0) && (prevE.WindDelta != 0) &&
                    SlopesEqual(new IntPoint(xPrev, pt.Y), prevE.Top, new IntPoint(xE, pt.Y), e.Top, m_UseFullRange))
                {
                    OutPt outPt = AddOutPt(ref prevE, pt);
                    AddJoin(ref result, ref outPt, e.Top);
                }
            }
            return result;
        }

        //------------------------------------------------------------------------------

        private OutPt AddOutPt(ref TEdge e, IntPoint pt)
        {
            if (e.OutIdx < 0)
            {
                OutRec outRec;
                CreateOutRec(out outRec);
                outRec.IsOpen = (e.WindDelta == 0);
                OutPt newOp = new OutPt();
                newOp.Initialize();
                outRec.Pts = newOp;
                newOp.Idx = outRec.Idx;
                newOp.Pt = pt;
                newOp.Next = newOp;
                newOp.Prev = newOp;
                if (!outRec.IsOpen)
                    SetHoleState(ref e, ref outRec);
                e.OutIdx = outRec.Idx; //nb: do this after SetZ !
                return newOp;
            }
            else
            {
                OutRec outRec = m_PolyOuts[e.OutIdx];
                //OutRec.Pts is the 'Left-most' point & OutRec.Pts.Prev is the 'Right-most'
                OutPt op = outRec.Pts;
                bool ToFront = (e.Side == EdgeSide.esLeft);
                if (ToFront && pt == op.Pt) return op;
                else if (!ToFront && pt == op.Prev.Pt) return op.Prev;

                OutPt newOp = new OutPt();
                newOp.Initialize();
                newOp.Idx = outRec.Idx;
                newOp.Pt = pt;
                newOp.Next = op;
                newOp.Prev = op.Prev;
                newOp.Prev.Next = newOp;
                op.Prev = newOp;
                if (ToFront) outRec.Pts = newOp;
                return newOp;
            }
        }

        //------------------------------------------------------------------------------

        private void GetLastOutPt(ref TEdge e, out OutPt outPt)
        {
            OutRec outRec = m_PolyOuts[e.OutIdx];
            if (e.Side == EdgeSide.esLeft)
                outPt = outRec.Pts;
            else
                outPt = outRec.Pts.Prev;
        }

        //------------------------------------------------------------------------------

        internal void SwapPoints(ref IntPoint pt1, ref IntPoint pt2)
        {
            IntPoint tmp = new IntPoint(pt1);
            pt1 = pt2;
            pt2 = tmp;
        }

        //------------------------------------------------------------------------------

        private bool HorzSegmentsOverlap(ClipInt seg1a, ClipInt seg1b, ClipInt seg2a, ClipInt seg2b)
        {
            if (seg1a > seg1b) Swap(ref seg1a, ref seg1b);
            if (seg2a > seg2b) Swap(ref seg2a, ref seg2b);
            return (seg1a < seg2b) && (seg2a < seg1b);
        }

        //------------------------------------------------------------------------------

        private void SetHoleState(ref TEdge e, ref OutRec outRec)
        {
            TEdge e2 = e.PrevInAEL;
            TEdge eTmp = new TEdge();
            eTmp.Initialize();
            while (e2.NotNull)
            {
                if (e2.OutIdx >= 0 && e2.WindDelta != 0)
                {
                    if (eTmp.IsNull)
                        eTmp = e2;
                    else if (eTmp.OutIdx == e2.OutIdx)
                        eTmp.SetNull(); //paired
                }
                e2 = e2.PrevInAEL;
            }

            if (eTmp.IsNull)
            {
                outRec.FirstLeft.SetNull();
                outRec.IsHole = false;
            }
            else
            {
                outRec.FirstLeft = m_PolyOuts[eTmp.OutIdx];
                outRec.IsHole = !outRec.FirstLeft.IsHole;
            }
        }

        //------------------------------------------------------------------------------

        private double GetDx(IntPoint pt1, IntPoint pt2)
        {
            if (pt1.Y == pt2.Y) return horizontal;
            else return (double)(pt2.X - pt1.X) / (pt2.Y - pt1.Y);
        }

        //---------------------------------------------------------------------------

        private bool FirstIsBottomPt(ref OutPt btmPt1, ref OutPt btmPt2)
        {
            OutPt p = btmPt1.Prev;
            while ((p.Pt == btmPt1.Pt) && (p != btmPt1)) p = p.Prev;
            double dx1p = Math.Abs(GetDx(btmPt1.Pt, p.Pt));
            p = btmPt1.Next;
            while ((p.Pt == btmPt1.Pt) && (p != btmPt1)) p = p.Next;
            double dx1n = Math.Abs(GetDx(btmPt1.Pt, p.Pt));

            p = btmPt2.Prev;
            while ((p.Pt == btmPt2.Pt) && (p != btmPt2)) p = p.Prev;
            double dx2p = Math.Abs(GetDx(btmPt2.Pt, p.Pt));
            p = btmPt2.Next;
            while ((p.Pt == btmPt2.Pt) && (p != btmPt2)) p = p.Next;
            double dx2n = Math.Abs(GetDx(btmPt2.Pt, p.Pt));

            if (Math.Max(dx1p, dx1n) == Math.Max(dx2p, dx2n) &&
                Math.Min(dx1p, dx1n) == Math.Min(dx2p, dx2n))
                return Area(ref btmPt1) > 0; //if otherwise identical use orientation
            else
                return (dx1p >= dx2p && dx1p >= dx2n) || (dx1n >= dx2p && dx1n >= dx2n);
        }

        //------------------------------------------------------------------------------

        private void GetBottomPt(ref OutPt pp, out OutPt outPt)
        {
            OutPt dups = new OutPt();
            dups.Initialize();
            OutPt p = pp.Next;
            while (p != pp)
            {
                if (p.Pt.Y > pp.Pt.Y)
                {
                    pp = p;
                    dups.SetNull();
                }
                else if (p.Pt.Y == pp.Pt.Y && p.Pt.X <= pp.Pt.X)
                {
                    if (p.Pt.X < pp.Pt.X)
                    {
                        dups.SetNull();
                        pp = p;
                    }
                    else
                    {
                        if (p.Next != pp && p.Prev != pp) dups = p;
                    }
                }
                p = p.Next;
            }
            if (dups.NotNull)
            {
                //there appears to be at least 2 vertices at bottomPt so ...
                while (dups != p)
                {
                    if (!FirstIsBottomPt(ref p, ref dups)) pp = dups;
                    dups = dups.Next;
                    while (dups.Pt != pp.Pt) dups = dups.Next;
                }
            }
            outPt = pp;
        }

        //------------------------------------------------------------------------------

        private void GetLowermostRec(ref OutRec outRec1, ref OutRec outRec2, out OutRec returnRec)
        {
            //work out which polygon fragment has the correct hole state ...
            if (outRec1.BottomPt.IsNull)
                GetBottomPt(ref outRec1.Pts, out outRec1.BottomPt);
            if (outRec2.BottomPt.IsNull)
                GetBottomPt(ref outRec2.Pts, out outRec2.BottomPt);
            OutPt bPt1 = outRec1.BottomPt;
            OutPt bPt2 = outRec2.BottomPt;
            if (bPt1.Pt.Y > bPt2.Pt.Y) returnRec = outRec1;
            else if (bPt1.Pt.Y < bPt2.Pt.Y) returnRec = outRec2;
            else if (bPt1.Pt.X < bPt2.Pt.X) returnRec = outRec1;
            else if (bPt1.Pt.X > bPt2.Pt.X) returnRec = outRec2;
            else if (bPt1.Next == bPt1) returnRec = outRec2;
            else if (bPt2.Next == bPt2) returnRec = outRec1;
            else if (FirstIsBottomPt(ref bPt1, ref bPt2)) returnRec = outRec1;
            else returnRec = outRec2;
        }

        //------------------------------------------------------------------------------

        bool OutRec1RightOfOutRec2(ref OutRec outRec1, ref OutRec outRec2)
        {
            do
            {
                outRec1 = outRec1.FirstLeft;
                if (outRec1 == outRec2) return true;
            }
            while (outRec1.NotNull);
            return false;
        }

        //------------------------------------------------------------------------------

        private void GetOutRec(int idx, out OutRec retRec)
        {
            OutRec outrec = m_PolyOuts[idx];
            while (outrec != m_PolyOuts[outrec.Idx])
                outrec = m_PolyOuts[outrec.Idx];
            retRec = outrec;
        }

        //------------------------------------------------------------------------------

        private void AppendPolygon(ref TEdge e1, ref TEdge e2)
        {
            OutRec outRec1 = m_PolyOuts[e1.OutIdx];
            OutRec outRec2 = m_PolyOuts[e2.OutIdx];

            OutRec holeStateRec;
            if (OutRec1RightOfOutRec2(ref outRec1, ref outRec2))
                holeStateRec = outRec2;
            else if (OutRec1RightOfOutRec2(ref outRec2, ref outRec1))
                holeStateRec = outRec1;
            else
                GetLowermostRec(ref outRec1, ref outRec2, out holeStateRec);

            //get the start and ends of both output polygons and
            //join E2 poly onto E1 poly and delete pointers to E2 ...
            OutPt p1_lft = outRec1.Pts;
            OutPt p1_rt = p1_lft.Prev;
            OutPt p2_lft = outRec2.Pts;
            OutPt p2_rt = p2_lft.Prev;

            //join e2 poly onto e1 poly and delete pointers to e2 ...
            if (e1.Side == EdgeSide.esLeft)
            {
                if (e2.Side == EdgeSide.esLeft)
                {
                    //z y x a b c
                    ReversePolyPtLinks(ref p2_lft);
                    p2_lft.Next = p1_lft;
                    p1_lft.Prev = p2_lft;
                    p1_rt.Next = p2_rt;
                    p2_rt.Prev = p1_rt;
                    outRec1.Pts = p2_rt;
                }
                else
                {
                    //x y z a b c
                    p2_rt.Next = p1_lft;
                    p1_lft.Prev = p2_rt;
                    p2_lft.Prev = p1_rt;
                    p1_rt.Next = p2_lft;
                    outRec1.Pts = p2_lft;
                }
            }
            else
            {
                if (e2.Side == EdgeSide.esRight)
                {
                    //a b c z y x
                    ReversePolyPtLinks(ref p2_lft);
                    p1_rt.Next = p2_rt;
                    p2_rt.Prev = p1_rt;
                    p2_lft.Next = p1_lft;
                    p1_lft.Prev = p2_lft;
                }
                else
                {
                    //a b c x y z
                    p1_rt.Next = p2_lft;
                    p2_lft.Prev = p1_rt;
                    p1_lft.Prev = p2_rt;
                    p2_rt.Next = p1_lft;
                }
            }

            outRec1.BottomPt.SetNull();
            if (holeStateRec == outRec2)
            {
                if (outRec2.FirstLeft != outRec1)
                    outRec1.FirstLeft = outRec2.FirstLeft;
                outRec1.IsHole = outRec2.IsHole;
            }
            outRec2.Pts.SetNull();
            outRec2.BottomPt.SetNull();

            outRec2.FirstLeft = outRec1;

            int OKIdx = e1.OutIdx;
            int ObsoleteIdx = e2.OutIdx;

            e1.OutIdx = Unassigned; //nb: safe because we only get here via AddLocalMaxPoly
            e2.OutIdx = Unassigned;

            TEdge e = m_ActiveEdges;
            while (e.NotNull)
            {
                if (e.OutIdx == ObsoleteIdx)
                {
                    e.OutIdx = OKIdx;
                    e.Side = e1.Side;
                    break;
                }
                e = e.NextInAEL;
            }
            outRec2.Idx = outRec1.Idx;
        }

        //------------------------------------------------------------------------------

        private void ReversePolyPtLinks(ref OutPt pp)
        {
            if (pp.IsNull) return;
            OutPt pp1;
            OutPt pp2;
            pp1 = pp;
            do
            {
                pp2 = pp1.Next;
                pp1.Next = pp1.Prev;
                pp1.Prev = pp2;
                pp1 = pp2;
            }
            while (pp1 != pp);
        }

        //------------------------------------------------------------------------------

        private static void SwapSides(ref TEdge edge1, ref TEdge edge2)
        {
            EdgeSide side = edge1.Side;
            edge1.Side = edge2.Side;
            edge2.Side = side;
        }

        //------------------------------------------------------------------------------

        private static void SwapPolyIndexes(ref TEdge edge1, ref TEdge edge2)
        {
            int outIdx = edge1.OutIdx;
            edge1.OutIdx = edge2.OutIdx;
            edge2.OutIdx = outIdx;
        }

        //------------------------------------------------------------------------------

        private void IntersectEdges(ref TEdge e1, ref TEdge e2, IntPoint pt)
        {
            //e1 will be to the left of e2 BELOW the intersection. Therefore e1 is before
            //e2 in AEL except when e1 is being inserted at the intersection point ...

            bool e1Contributing = (e1.OutIdx >= 0);
            bool e2Contributing = (e2.OutIdx >= 0);

#if use_lines
            //if either edge is on an OPEN path ...
            if (e1.WindDelta == 0 || e2.WindDelta == 0)
            {
                //ignore subject-subject open path intersections UNLESS they
                //are both open paths, AND they are both 'contributing maximas' ...
                if (e1.WindDelta == 0 && e2.WindDelta == 0) return;
                //if intersecting a subj line with a subj poly ...
                else if (e1.PolyTyp == e2.PolyTyp &&
                         e1.WindDelta != e2.WindDelta && m_ClipType == ClipType.ctUnion)
                {
                    if (e1.WindDelta == 0)
                    {
                        if (e2Contributing)
                        {
                            AddOutPt(ref e1, pt);
                            if (e1Contributing) e1.OutIdx = Unassigned;
                        }
                    }
                    else
                    {
                        if (e1Contributing)
                        {
                            AddOutPt(ref e2, pt);
                            if (e2Contributing) e2.OutIdx = Unassigned;
                        }
                    }
                }
                else if (e1.PolyTyp != e2.PolyTyp)
                {
                    if ((e1.WindDelta == 0) && Math.Abs(e2.WindCnt) == 1 &&
                        (m_ClipType != ClipType.ctUnion || e2.WindCnt2 == 0))
                    {
                        AddOutPt(ref e1, pt);
                        if (e1Contributing) e1.OutIdx = Unassigned;
                    }
                    else if ((e2.WindDelta == 0) && (Math.Abs(e1.WindCnt) == 1) &&
                             (m_ClipType != ClipType.ctUnion || e1.WindCnt2 == 0))
                    {
                        AddOutPt(ref e2, pt);
                        if (e2Contributing) e2.OutIdx = Unassigned;
                    }
                }
                return;
            }
#endif

            //update winding counts...
            //assumes that e1 will be to the Right of e2 ABOVE the intersection
            if (e1.PolyTyp == e2.PolyTyp)
            {
                if (IsEvenOddFillType(ref e1))
                {
                    int oldE1WindCnt = e1.WindCnt;
                    e1.WindCnt = e2.WindCnt;
                    e2.WindCnt = oldE1WindCnt;
                }
                else
                {
                    if (e1.WindCnt + e2.WindDelta == 0) e1.WindCnt = -e1.WindCnt;
                    else e1.WindCnt += e2.WindDelta;
                    if (e2.WindCnt - e1.WindDelta == 0) e2.WindCnt = -e2.WindCnt;
                    else e2.WindCnt -= e1.WindDelta;
                }
            }
            else
            {
                if (!IsEvenOddFillType(ref e2)) e1.WindCnt2 += e2.WindDelta;
                else e1.WindCnt2 = (e1.WindCnt2 == 0) ? 1 : 0;
                if (!IsEvenOddFillType(ref e1)) e2.WindCnt2 -= e1.WindDelta;
                else e2.WindCnt2 = (e2.WindCnt2 == 0) ? 1 : 0;
            }

            PolyFillType e1FillType, e2FillType, e1FillType2, e2FillType2;
            if (e1.PolyTyp == PolyType.ptSubject)
            {
                e1FillType = m_SubjFillType;
                e1FillType2 = m_ClipFillType;
            }
            else
            {
                e1FillType = m_ClipFillType;
                e1FillType2 = m_SubjFillType;
            }
            if (e2.PolyTyp == PolyType.ptSubject)
            {
                e2FillType = m_SubjFillType;
                e2FillType2 = m_ClipFillType;
            }
            else
            {
                e2FillType = m_ClipFillType;
                e2FillType2 = m_SubjFillType;
            }

            int e1Wc, e2Wc;
            switch (e1FillType)
            {
                case PolyFillType.pftPositive: e1Wc = e1.WindCnt; break;
                case PolyFillType.pftNegative: e1Wc = -e1.WindCnt; break;
                default: e1Wc = Math.Abs(e1.WindCnt); break;
            }
            switch (e2FillType)
            {
                case PolyFillType.pftPositive: e2Wc = e2.WindCnt; break;
                case PolyFillType.pftNegative: e2Wc = -e2.WindCnt; break;
                default: e2Wc = Math.Abs(e2.WindCnt); break;
            }

            if (e1Contributing && e2Contributing)
            {
                if ((e1Wc != 0 && e1Wc != 1) || (e2Wc != 0 && e2Wc != 1) ||
                    (e1.PolyTyp != e2.PolyTyp && m_ClipType != ClipType.ctXor))
                {
                    AddLocalMaxPoly(ref e1, ref e2, pt);
                }
                else
                {
                    AddOutPt(ref e1, pt);
                    AddOutPt(ref e2, pt);
                    SwapSides(ref e1, ref e2);
                    SwapPolyIndexes(ref e1, ref e2);
                }
            }
            else if (e1Contributing)
            {
                if (e2Wc == 0 || e2Wc == 1)
                {
                    AddOutPt(ref e1, pt);
                    SwapSides(ref e1, ref e2);
                    SwapPolyIndexes(ref e1, ref e2);
                }
            }
            else if (e2Contributing)
            {
                if (e1Wc == 0 || e1Wc == 1)
                {
                    AddOutPt(ref e2, pt);
                    SwapSides(ref e1, ref e2);
                    SwapPolyIndexes(ref e1, ref e2);
                }
            }
            else if ((e1Wc == 0 || e1Wc == 1) && (e2Wc == 0 || e2Wc == 1))
            {
                //neither edge is currently contributing ...
                ClipInt e1Wc2, e2Wc2;
                switch (e1FillType2)
                {
                    case PolyFillType.pftPositive: e1Wc2 = e1.WindCnt2; break;
                    case PolyFillType.pftNegative: e1Wc2 = -e1.WindCnt2; break;
                    default: e1Wc2 = Math.Abs(e1.WindCnt2); break;
                }
                switch (e2FillType2)
                {
                    case PolyFillType.pftPositive: e2Wc2 = e2.WindCnt2; break;
                    case PolyFillType.pftNegative: e2Wc2 = -e2.WindCnt2; break;
                    default: e2Wc2 = Math.Abs(e2.WindCnt2); break;
                }

                if (e1.PolyTyp != e2.PolyTyp)
                {
                    AddLocalMinPoly(ref e1, ref e2, pt);
                }
                else if (e1Wc == 1 && e2Wc == 1)
                    switch (m_ClipType)
                    {
                        case ClipType.ctIntersection:
                            if (e1Wc2 > 0 && e2Wc2 > 0)
                                AddLocalMinPoly(ref e1, ref e2, pt);
                            break;
                        case ClipType.ctUnion:
                            if (e1Wc2 <= 0 && e2Wc2 <= 0)
                                AddLocalMinPoly(ref e1, ref e2, pt);
                            break;
                        case ClipType.ctDifference:
                            if (((e1.PolyTyp == PolyType.ptClip) && (e1Wc2 > 0) && (e2Wc2 > 0)) ||
                                ((e1.PolyTyp == PolyType.ptSubject) && (e1Wc2 <= 0) && (e2Wc2 <= 0)))
                                AddLocalMinPoly(ref e1, ref e2, pt);
                            break;
                        case ClipType.ctXor:
                            AddLocalMinPoly(ref e1, ref e2, pt);
                            break;
                    }
                else
                    SwapSides(ref e1, ref e2);
            }
        }

        //------------------------------------------------------------------------------

        private void DeleteFromSEL(ref TEdge e)
        {
            TEdge SelPrev = e.PrevInSEL;
            TEdge SelNext = e.NextInSEL;
            if (SelPrev.IsNull && SelNext.IsNull && (e != m_SortedEdges))
                return; //already deleted
            if (SelPrev.NotNull)
                SelPrev.NextInSEL = SelNext;
            else m_SortedEdges = SelNext;
            if (SelNext.NotNull)
                SelNext.PrevInSEL = SelPrev;
            e.NextInSEL.SetNull();
            e.PrevInSEL.SetNull();
        }

        //------------------------------------------------------------------------------

        private void ProcessHorizontals()
        {
            TEdge horzEdge; //m_SortedEdges;
            while (PopEdgeFromSEL(out horzEdge))
                ProcessHorizontal(ref horzEdge);
        }

        //------------------------------------------------------------------------------

        void GetHorzDirection(ref TEdge HorzEdge, out Direction Dir, out ClipInt Left, out ClipInt Right)
        {
            if (HorzEdge.Bot.X < HorzEdge.Top.X)
            {
                Left = HorzEdge.Bot.X;
                Right = HorzEdge.Top.X;
                Dir = Direction.dLeftToRight;
            }
            else
            {
                Left = HorzEdge.Top.X;
                Right = HorzEdge.Bot.X;
                Dir = Direction.dRightToLeft;
            }
        }

        //------------------------------------------------------------------------

        private void ProcessHorizontal(ref TEdge horzEdge)
        {
            Direction dir;
            ClipInt horzLeft, horzRight;
            bool IsOpen = horzEdge.WindDelta == 0;

            GetHorzDirection(ref horzEdge, out dir, out horzLeft, out horzRight);

            TEdge eLastHorz = horzEdge, eMaxPair = new TEdge();
            eMaxPair.Initialize();
            while (eLastHorz.NextInLML.NotNull && IsHorizontal(ref eLastHorz.NextInLML))
                eLastHorz = eLastHorz.NextInLML;
            if (eLastHorz.NextInLML.IsNull)
                GetMaximaPair(ref eLastHorz, out eMaxPair);

            Maxima currMax = m_Maxima;
            if (currMax.NotNull)
            {
                //get the first maxima in range (X) ...
                if (dir == Direction.dLeftToRight)
                {
                    while (currMax.NotNull && currMax.X <= horzEdge.Bot.X)
                        currMax = currMax.Next;
                    if (currMax.NotNull && currMax.X >= eLastHorz.Top.X)
                        currMax.SetNull();
                }
                else
                {
                    while (currMax.Next.NotNull && currMax.Next.X < horzEdge.Bot.X)
                        currMax = currMax.Next;
                    if (currMax.X <= eLastHorz.Top.X) currMax.SetNull();
                }
            }

            OutPt op1 = new OutPt();
            op1.Initialize();
            for (; ; ) //loop through consec. horizontal edges
            {
                bool IsLastHorz = (horzEdge == eLastHorz);
                TEdge e;
                GetNextInAEL(ref horzEdge, dir, out e);
                while (e.NotNull)
                {
                    //this code block inserts extra coords into horizontal edges (in output
                    //polygons) whereever maxima touch these horizontal edges. This helps
                    //'simplifying' polygons (ie if the Simplify property is set).
                    if (currMax.NotNull)
                    {
                        if (dir == Direction.dLeftToRight)
                        {
                            while (currMax.NotNull && currMax.X < e.Curr.X)
                            {
                                if (horzEdge.OutIdx >= 0 && !IsOpen)
                                    AddOutPt(ref horzEdge, new IntPoint(currMax.X, horzEdge.Bot.Y));
                                currMax = currMax.Next;
                            }
                        }
                        else
                        {
                            while (currMax.NotNull && currMax.X > e.Curr.X)
                            {
                                if (horzEdge.OutIdx >= 0 && !IsOpen)
                                    AddOutPt(ref horzEdge, new IntPoint(currMax.X, horzEdge.Bot.Y));
                                currMax = currMax.Prev;
                            }
                        }
                    }

                    if ((dir == Direction.dLeftToRight && e.Curr.X > horzRight) ||
                        (dir == Direction.dRightToLeft && e.Curr.X < horzLeft)) break;

                    //Also break if we've got to the end of an intermediate horizontal edge ...
                    //nb: Smaller Dx's are to the right of larger Dx's ABOVE the horizontal.
                    if (e.Curr.X == horzEdge.Top.X && horzEdge.NextInLML.NotNull &&
                        e.Dx < horzEdge.NextInLML.Dx) break;

                    if (horzEdge.OutIdx >= 0 && !IsOpen) //note: may be done multiple times
                    {
                        op1 = AddOutPt(ref horzEdge, e.Curr);
                        TEdge eNextHorz = m_SortedEdges;
                        while (eNextHorz.NotNull)
                        {
                            if (eNextHorz.OutIdx >= 0 &&
                                HorzSegmentsOverlap(horzEdge.Bot.X,
                                    horzEdge.Top.X, eNextHorz.Bot.X, eNextHorz.Top.X))
                            {
                                OutPt op2;
                                GetLastOutPt(ref eNextHorz, out op2);
                                AddJoin(ref op2, ref op1, eNextHorz.Top);
                            }
                            eNextHorz = eNextHorz.NextInSEL;
                        }
                        AddGhostJoin(ref op1, horzEdge.Bot);
                    }

                    //OK, so far we're still in range of the horizontal Edge  but make sure
                    //we're at the last of consec. horizontals when matching with eMaxPair
                    if (e == eMaxPair && IsLastHorz)
                    {
                        if (horzEdge.OutIdx >= 0)
                            AddLocalMaxPoly(ref horzEdge, ref eMaxPair, horzEdge.Top);
                        DeleteFromAEL(ref horzEdge);
                        DeleteFromAEL(ref eMaxPair);
                        return;
                    }

                    if (dir == Direction.dLeftToRight)
                    {
                        IntPoint Pt = new IntPoint(e.Curr.X, horzEdge.Curr.Y);
                        IntersectEdges(ref horzEdge, ref e, Pt);
                    }
                    else
                    {
                        IntPoint Pt = new IntPoint(e.Curr.X, horzEdge.Curr.Y);
                        IntersectEdges(ref e, ref horzEdge, Pt);
                    }
                    TEdge eNext;
                    GetNextInAEL(ref e, dir, out eNext);
                    SwapPositionsInAEL(ref horzEdge, ref e);
                    e = eNext;
                } //end while(e.NotNull)

                //Break out of loop if HorzEdge.NextInLML is not also horizontal ...
                if (horzEdge.NextInLML.IsNull || !IsHorizontal(ref horzEdge.NextInLML)) break;

                UpdateEdgeIntoAEL(ref horzEdge);
                if (horzEdge.OutIdx >= 0) AddOutPt(ref horzEdge, horzEdge.Bot);
                GetHorzDirection(ref horzEdge, out dir, out horzLeft, out horzRight);
            } //end for (;;)

            if (horzEdge.OutIdx >= 0 && op1.IsNull)
            {
                GetLastOutPt(ref horzEdge, out op1);
                TEdge eNextHorz = m_SortedEdges;
                while (eNextHorz.NotNull)
                {
                    if (eNextHorz.OutIdx >= 0 &&
                        HorzSegmentsOverlap(horzEdge.Bot.X,
                            horzEdge.Top.X, eNextHorz.Bot.X, eNextHorz.Top.X))
                    {
                        OutPt op2;
                        GetLastOutPt(ref eNextHorz, out op2);
                        AddJoin(ref op2, ref op1, eNextHorz.Top);
                    }
                    eNextHorz = eNextHorz.NextInSEL;
                }
                AddGhostJoin(ref op1, horzEdge.Top);
            }

            if (horzEdge.NextInLML.NotNull)
            {
                if (horzEdge.OutIdx >= 0)
                {
                    op1 = AddOutPt(ref horzEdge, horzEdge.Top);

                    UpdateEdgeIntoAEL(ref horzEdge);
                    if (horzEdge.WindDelta == 0) return;
                    //nb: HorzEdge is no longer horizontal here
                    TEdge ePrev = horzEdge.PrevInAEL;
                    TEdge eNext = horzEdge.NextInAEL;
                    if (ePrev.NotNull && ePrev.Curr.X == horzEdge.Bot.X &&
                        ePrev.Curr.Y == horzEdge.Bot.Y && ePrev.WindDelta != 0 &&
                        (ePrev.OutIdx >= 0 && ePrev.Curr.Y > ePrev.Top.Y &&
                         SlopesEqual(ref horzEdge, ref ePrev, m_UseFullRange)))
                    {
                        OutPt op2 = AddOutPt(ref ePrev, horzEdge.Bot);
                        AddJoin(ref op1, ref op2, horzEdge.Top);
                    }
                    else if (eNext.NotNull && eNext.Curr.X == horzEdge.Bot.X &&
                             eNext.Curr.Y == horzEdge.Bot.Y && eNext.WindDelta != 0 &&
                             eNext.OutIdx >= 0 && eNext.Curr.Y > eNext.Top.Y &&
                             SlopesEqual(ref horzEdge, ref eNext, m_UseFullRange))
                    {
                        OutPt op2 = AddOutPt(ref eNext, horzEdge.Bot);
                        AddJoin(ref op1, ref op2, horzEdge.Top);
                    }
                }
                else
                    UpdateEdgeIntoAEL(ref horzEdge);
            }
            else
            {
                if (horzEdge.OutIdx >= 0) AddOutPt(ref horzEdge, horzEdge.Top);
                DeleteFromAEL(ref horzEdge);
            }
        }

        //------------------------------------------------------------------------------

        private void GetNextInAEL(ref TEdge e, Direction Direction, out TEdge outEdge)
        {
            outEdge = Direction == Direction.dLeftToRight ? e.NextInAEL : e.PrevInAEL;
        }

        //------------------------------------------------------------------------------

        private bool IsMinima(ref TEdge e)
        {
            return e.NotNull && (e.Prev.NextInLML != e) && (e.Next.NextInLML != e);
        }

        //------------------------------------------------------------------------------

        private bool IsMaxima(ref TEdge e, double Y)
        {
            return (e.NotNull && e.Top.Y == Y && e.NextInLML.IsNull);
        }

        //------------------------------------------------------------------------------

        private bool IsIntermediate(ref TEdge e, double Y)
        {
            return (e.Top.Y == Y && e.NextInLML.NotNull);
        }

        //------------------------------------------------------------------------------

        internal void GetMaximaPair(ref TEdge e, out TEdge outEdge)
        {
            if ((e.Next.Top == e.Top) && e.Next.NextInLML.IsNull)
                outEdge = e.Next;
            else if ((e.Prev.Top == e.Top) && e.Prev.NextInLML.IsNull)
                outEdge = e.Prev;
            else
                outEdge = NULL_TEdge;
        }

        //------------------------------------------------------------------------------

        internal TEdge GetMaximaPairEx(ref TEdge e)
        {
            //as above but returns null if MaxPair isn't in AEL (unless it's horizontal)
            TEdge result;
            GetMaximaPair(ref e, out result);
            if (result.IsNull || result.OutIdx == Skip ||
                ((result.NextInAEL == result.PrevInAEL) && !IsHorizontal(ref result))) return NULL_TEdge;
            return result;
        }

        //------------------------------------------------------------------------------

        private bool ProcessIntersections(ClipInt topY)
        {
            if (m_ActiveEdges.IsNull) return true;
            try
            {
                BuildIntersectList(topY);
                if (m_IntersectList.Length == 0) return true;
                if (m_IntersectList.Length == 1 || FixupIntersectionOrder())
                    ProcessIntersectList();
                else
                    return false;
            }
            catch
            {
                m_SortedEdges.SetNull();
                m_IntersectList.Clear();
                throw new ClipperException("ProcessIntersections error");
            }
            m_SortedEdges.SetNull();
            return true;
        }

        //------------------------------------------------------------------------------

        private void BuildIntersectList(ClipInt topY)
        {
            if (m_ActiveEdges.IsNull) return;

            //prepare for sorting ...
            TEdge e = m_ActiveEdges;
            m_SortedEdges = e;
            while (e.NotNull)
            {
                e.PrevInSEL = e.PrevInAEL;
                e.NextInSEL = e.NextInAEL;
                e.Curr.X = TopX(ref e, topY);
                e = e.NextInAEL;
            }

            //bubblesort ...
            bool isModified = true;
            while (isModified && m_SortedEdges.NotNull)
            {
                isModified = false;
                e = m_SortedEdges;
                while (e.NextInSEL.NotNull)
                {
                    TEdge eNext = e.NextInSEL;
                    IntPoint pt;
                    if (e.Curr.X > eNext.Curr.X)
                    {
                        IntersectPoint(ref e, ref eNext, out pt);
                        if (pt.Y < topY)
                            pt = new IntPoint(TopX(ref e, topY), topY);
                        IntersectNode newNode = new IntersectNode();
                        newNode.Initialize();
                        newNode.Edge1 = e;
                        newNode.Edge2 = eNext;
                        newNode.Pt = pt;
                        m_IntersectList.Add(newNode);

                        SwapPositionsInSEL(ref e, ref eNext);
                        isModified = true;
                    }
                    else
                        e = eNext;
                }
                if (e.PrevInSEL.NotNull) e.PrevInSEL.NextInSEL.SetNull();
                else break;
            }
            m_SortedEdges.SetNull();
        }

        //------------------------------------------------------------------------------

        private bool EdgesAdjacent(ref IntersectNode inode)
        {
            return (inode.Edge1.NextInSEL == inode.Edge2) ||
                (inode.Edge1.PrevInSEL == inode.Edge2);
        }

        //------------------------------------------------------------------------------

        private static int IntersectNodeSort(ref IntersectNode node1, ref IntersectNode node2)
        {
            //the following typecast is safe because the differences in Pt.Y will
            //be limited to the height of the scanbeam.
            return (int)(node2.Pt.Y - node1.Pt.Y);
        }

        //------------------------------------------------------------------------------

        private bool FixupIntersectionOrder()
        {
            //pre-condition: intersections are sorted bottom-most first.
            //Now it's crucial that intersections are made only between adjacent edges,
            //so to ensure this the order of intersections may need adjusting ...
            m_IntersectList.Sort(m_IntersectNodeComparer);

            CopyAELToSEL();
            int cnt = m_IntersectList.Length;
            for (int i = 0; i < cnt; i++)
            {
                if (!EdgesAdjacent(ref m_IntersectList.GetIndexByRef(i)))
                {
                    int j = i + 1;
                    while (j < cnt && !EdgesAdjacent(ref m_IntersectList.GetIndexByRef(j))) j++;
                    if (j == cnt) return false;

                    IntersectNode tmp = m_IntersectList[i];
                    m_IntersectList[i] = m_IntersectList[j];
                    m_IntersectList[j] = tmp;
                }
                SwapPositionsInSEL(ref m_IntersectList[i].Edge1, ref m_IntersectList[i].Edge2);
            }
            return true;
        }

        //------------------------------------------------------------------------------

        private void ProcessIntersectList()
        {
            for (int i = 0; i < m_IntersectList.Length; i++)
            {
                IntersectNode iNode = m_IntersectList[i];
                {
                    IntersectEdges(ref iNode.Edge1, ref iNode.Edge2, iNode.Pt);
                    SwapPositionsInAEL(ref iNode.Edge1, ref iNode.Edge2);
                }
            }
            m_IntersectList.Clear();
        }

        //------------------------------------------------------------------------------

        internal static ClipInt Round(double value)
        {
            return value < 0 ? (ClipInt)(value - 0.5) : (ClipInt)(value + 0.5);
        }

        //------------------------------------------------------------------------------

        private static ClipInt TopX(ref TEdge edge, ClipInt currentY)
        {
            if (currentY == edge.Top.Y)
                return edge.Top.X;
            return edge.Bot.X + Round(edge.Dx * (currentY - edge.Bot.Y));
        }

        //------------------------------------------------------------------------------

        private void IntersectPoint(ref TEdge edge1, ref TEdge edge2, out IntPoint ip)
        {
            ip = new IntPoint();
            long pivotPoint = -1;
            bool isClamp = (edge2.Curr.N > 0 && edge2.Curr.N < LastIndex) && (edge1.Curr.N > 0 && edge1.Curr.N < LastIndex);
            if (edge1.Curr.N > edge2.Curr.N)
            {
                if (edge2.Curr.N != -1)
                {
                    if (isClamp)
                    {
                        pivotPoint = (edge1.Curr.N > 0) ? edge1.Curr.N - 1 : 0;
                    }
                }
                else
                {
                    pivotPoint = edge1.Curr.N;
                }
            }
            else
            {
                if (edge1.Curr.N != -1)
                {
                    if (isClamp)
                    {
                        pivotPoint = edge2.Curr.N;
                    }
                }
                else
                {
                    pivotPoint = (edge2.Curr.N > 0) ? edge2.Curr.N - 1 : 0;
                }
            }
            ip.D = 2; ip.N = isClamp ? pivotPoint : -1;

            //nb: with very large coordinate values, it's possible for SlopesEqual() to
            //return false but for the edge.Dx value be equal due to double precision rounding.
            if (edge1.Dx == edge2.Dx)
            {
                ip.Y = edge1.Curr.Y;
                ip.X = TopX(ref edge1, ip.Y);
                return;
            }

            double b1, b2;
            if (edge1.Delta.X == 0)
            {
                ip.X = edge1.Bot.X;
                if (IsHorizontal(ref edge2))
                {
                    ip.Y = edge2.Bot.Y;
                }
                else
                {
                    b2 = edge2.Bot.Y - (edge2.Bot.X / edge2.Dx);
                    ip.Y = Round(ip.X / edge2.Dx + b2);
                }
            }
            else if (edge2.Delta.X == 0)
            {
                ip.X = edge2.Bot.X;
                if (IsHorizontal(ref edge1))
                {
                    ip.Y = edge1.Bot.Y;
                }
                else
                {
                    b1 = edge1.Bot.Y - (edge1.Bot.X / edge1.Dx);
                    ip.Y = Round(ip.X / edge1.Dx + b1);
                }
            }
            else
            {
                b1 = edge1.Bot.X - edge1.Bot.Y * edge1.Dx;
                b2 = edge2.Bot.X - edge2.Bot.Y * edge2.Dx;
                double q = (b2 - b1) / (edge1.Dx - edge2.Dx);
                ip.Y = Round(q);
                if (Math.Abs(edge1.Dx) < Math.Abs(edge2.Dx))
                    ip.X = Round(edge1.Dx * q + b1);
                else
                    ip.X = Round(edge2.Dx * q + b2);
            }

            if (ip.Y < edge1.Top.Y || ip.Y < edge2.Top.Y)
            {
                if (edge1.Top.Y > edge2.Top.Y)
                    ip.Y = edge1.Top.Y;
                else
                    ip.Y = edge2.Top.Y;
                if (Math.Abs(edge1.Dx) < Math.Abs(edge2.Dx))
                    ip.X = TopX(ref edge1, ip.Y);
                else
                    ip.X = TopX(ref edge2, ip.Y);
            }
            //finally, don't allow 'ip' to be BELOW curr.Y (ie bottom of scanbeam) ...
            if (ip.Y > edge1.Curr.Y)
            {
                ip.Y = edge1.Curr.Y;
                //better to use the more vertical edge to derive X ...
                if (Math.Abs(edge1.Dx) > Math.Abs(edge2.Dx))
                    ip.X = TopX(ref edge2, ip.Y);
                else
                    ip.X = TopX(ref edge1, ip.Y);
            }
        }

        //------------------------------------------------------------------------------

        private void ProcessEdgesAtTopOfScanbeam(ClipInt topY)
        {
            TEdge e = m_ActiveEdges;
            while (e.NotNull)
            {
                //1. process maxima, treating them as if they're 'bent' horizontal edges,
                //   but exclude maxima with horizontal edges. nb: e can't be a horizontal.
                bool IsMaximaEdge = IsMaxima(ref e, topY);

                if (IsMaximaEdge)
                {
                    TEdge eMaxPair = GetMaximaPairEx(ref e);
                    IsMaximaEdge = (eMaxPair.IsNull || !IsHorizontal(ref eMaxPair));
                }

                if (IsMaximaEdge)
                {
                    if (StrictlySimple) InsertMaxima(e.Top.X);
                    TEdge ePrev = e.PrevInAEL;
                    DoMaxima(ref e);
                    if (ePrev.IsNull) e = m_ActiveEdges;
                    else e = ePrev.NextInAEL;
                }
                else
                {
                    //2. promote horizontal edges, otherwise update Curr.X and Curr.Y ...
                    if (IsIntermediate(ref e, topY) && IsHorizontal(ref e.NextInLML))
                    {
                        UpdateEdgeIntoAEL(ref e);
                        if (e.OutIdx >= 0)
                            AddOutPt(ref e, e.Bot);
                        AddEdgeToSEL(ref e);
                    }
                    else
                    {
                        e.Curr.X = TopX(ref e, topY);
                        e.Curr.Y = topY;
                    }
                    //When StrictlySimple and 'e' is being touched by another edge, then
                    //make sure both edges have a vertex here ...
                    if (StrictlySimple)
                    {
                        TEdge ePrev = e.PrevInAEL;
                        if ((e.OutIdx >= 0) && (e.WindDelta != 0) && ePrev.NotNull &&
                            (ePrev.OutIdx >= 0) && (ePrev.Curr.X == e.Curr.X) &&
                            (ePrev.WindDelta != 0))
                        {
                            IntPoint ip = new IntPoint(e.Curr);
                            OutPt op = AddOutPt(ref ePrev, ip);
                            OutPt op2 = AddOutPt(ref e, ip);
                            AddJoin(ref op, ref op2, ip); //StrictlySimple (type-3) join
                        }
                    }

                    e = e.NextInAEL;
                }
            }

            //3. Process horizontals at the Top of the scanbeam ...
            ProcessHorizontals();
            m_Maxima.SetNull();

            //4. Promote intermediate vertices ...
            e = m_ActiveEdges;
            while (e.NotNull)
            {
                if (IsIntermediate(ref e, topY))
                {
                    OutPt op = new OutPt();
                    op.Initialize();
                    if (e.OutIdx >= 0)
                        op = AddOutPt(ref e, e.Top);
                    UpdateEdgeIntoAEL(ref e);

                    //if output polygons share an edge, they'll need joining later ...
                    TEdge ePrev = e.PrevInAEL;
                    TEdge eNext = e.NextInAEL;
                    if (ePrev.NotNull && ePrev.Curr.X == e.Bot.X &&
                        ePrev.Curr.Y == e.Bot.Y && op.NotNull &&
                        ePrev.OutIdx >= 0 && ePrev.Curr.Y > ePrev.Top.Y &&
                        SlopesEqual(e.Curr, e.Top, ePrev.Curr, ePrev.Top, m_UseFullRange) &&
                        (e.WindDelta != 0) && (ePrev.WindDelta != 0))
                    {
                        OutPt op2 = AddOutPt(ref ePrev, e.Bot);
                        AddJoin(ref op, ref op2, e.Top);
                    }
                    else if (eNext.NotNull && eNext.Curr.X == e.Bot.X &&
                             eNext.Curr.Y == e.Bot.Y && op.NotNull &&
                             eNext.OutIdx >= 0 && eNext.Curr.Y > eNext.Top.Y &&
                             SlopesEqual(e.Curr, e.Top, eNext.Curr, eNext.Top, m_UseFullRange) &&
                             (e.WindDelta != 0) && (eNext.WindDelta != 0))
                    {
                        OutPt op2 = AddOutPt(ref eNext, e.Bot);
                        AddJoin(ref op, ref op2, e.Top);
                    }
                }
                e = e.NextInAEL;
            }
        }

        //------------------------------------------------------------------------------

        private void DoMaxima(ref TEdge e)
        {
            TEdge eMaxPair = GetMaximaPairEx(ref e);
            if (eMaxPair.IsNull)
            {
                if (e.OutIdx >= 0)
                    AddOutPt(ref e, e.Top);
                DeleteFromAEL(ref e);
                return;
            }

            TEdge eNext = e.NextInAEL;
            while (eNext.NotNull && eNext != eMaxPair)
            {
                IntersectEdges(ref e, ref eNext, e.Top);
                SwapPositionsInAEL(ref e, ref eNext);
                eNext = e.NextInAEL;
            }

            if (e.OutIdx == Unassigned && eMaxPair.OutIdx == Unassigned)
            {
                DeleteFromAEL(ref e);
                DeleteFromAEL(ref eMaxPair);
            }
            else if (e.OutIdx >= 0 && eMaxPair.OutIdx >= 0)
            {
                if (e.OutIdx >= 0) AddLocalMaxPoly(ref e, ref eMaxPair, e.Top);
                DeleteFromAEL(ref e);
                DeleteFromAEL(ref eMaxPair);
            }
#if use_lines
            else if (e.WindDelta == 0)
            {
                if (e.OutIdx >= 0)
                {
                    AddOutPt(ref e, e.Top);
                    e.OutIdx = Unassigned;
                }
                DeleteFromAEL(ref e);

                if (eMaxPair.OutIdx >= 0)
                {
                    AddOutPt(ref eMaxPair, e.Top);
                    eMaxPair.OutIdx = Unassigned;
                }
                DeleteFromAEL(ref eMaxPair);
            }
#endif
            else throw new ClipperException("DoMaxima error");
        }

        //------------------------------------------------------------------------------

        public static void ReversePaths(ref Paths polys)
        {
            for(int i=0;i<polys.Length;i++)
            {
                var poly = polys[i];
                poly.Reverse();
            }
        }

        //------------------------------------------------------------------------------

        public static bool Orientation(ref Path poly)
        {
            return Area(ref poly) >= 0;
        }

        //------------------------------------------------------------------------------

        private int PointCount(ref OutPt pts)
        {
            if (pts.IsNull) return 0;
            int result = 0;
            OutPt p = pts;
            do
            {
                result++;
                p = p.Next;
            }
            while (p != pts);
            return result;
        }

        //------------------------------------------------------------------------------

        private void BuildResult(ref Paths polyg)
        {
            polyg.Clear();
            polyg.Capacity = m_PolyOuts.Length;
            for (int i = 0; i < m_PolyOuts.Length; i++)
            {
                OutRec outRec = m_PolyOuts[i];
                if (outRec.Pts.IsNull) continue;
                OutPt p = outRec.Pts.Prev;
                int cnt = PointCount(ref p);
                if (cnt < 2) continue;
                Path pg = new Path(cnt, Allocator.Temp, NativeArrayOptions.ClearMemory);
                for (int j = 0; j < cnt; j++)
                {
                    pg.Add(p.Pt);
                    p = p.Prev;
                }
                polyg.Add(pg);
            }
        }

        //------------------------------------------------------------------------------

        private void FixupOutPolyline(ref OutRec outrec)
        {
            OutPt pp = outrec.Pts;
            OutPt lastPP = pp.Prev;
            while (pp != lastPP)
            {
                pp = pp.Next;
                if (pp.Pt == pp.Prev.Pt)
                {
                    if (pp == lastPP) lastPP = pp.Prev;
                    OutPt tmpPP = pp.Prev;
                    tmpPP.Next = pp.Next;
                    pp.Next.Prev = tmpPP;
                    pp = tmpPP;
                }
            }
            if (pp == pp.Prev) outrec.Pts.SetNull();
        }

        //------------------------------------------------------------------------------

        private void FixupOutPolygon(ref OutRec outRec)
        {
            //FixupOutPolygon() - removes duplicate points and simplifies consecutive
            //parallel edges by removing the middle vertex.
            OutPt lastOK = new OutPt();
            lastOK.Initialize();
            outRec.BottomPt.SetNull();
            OutPt pp = outRec.Pts;
            bool preserveCol = PreserveCollinear || StrictlySimple;
            for (; ; )
            {
                if (pp.Prev == pp || pp.Prev == pp.Next)
                {
                    outRec.Pts.SetNull();
                    return;
                }
                //test for duplicate points and collinear edges ...
                if ((pp.Pt == pp.Next.Pt) || (pp.Pt == pp.Prev.Pt) ||
                    (SlopesEqual(pp.Prev.Pt, pp.Pt, pp.Next.Pt, m_UseFullRange) &&
                     (!preserveCol || !Pt2IsBetweenPt1AndPt3(pp.Prev.Pt, pp.Pt, pp.Next.Pt))))
                {
                    lastOK.SetNull();
                    pp.Prev.Next = pp.Next;
                    pp.Next.Prev = pp.Prev;
                    pp = pp.Prev;
                }
                else if (pp == lastOK) break;
                else
                {
                    if (lastOK.IsNull) lastOK = pp;
                    pp = pp.Next;
                }
            }
            outRec.Pts = pp;
        }

        //------------------------------------------------------------------------------

        void DupOutPt(ref OutPt outPt, bool InsertAfter, out OutPt result)
        {
            result = new OutPt();
            result.Initialize();
            result.Pt = outPt.Pt;
            result.Idx = outPt.Idx;
            if (InsertAfter)
            {
                result.Next = outPt.Next;
                result.Prev = outPt;
                outPt.Next.Prev = result;
                outPt.Next = result;
            }
            else
            {
                result.Prev = outPt.Prev;
                result.Next = outPt;
                outPt.Prev.Next = result;
                outPt.Prev = result;
            }
        }

        //------------------------------------------------------------------------------

        bool GetOverlap(ClipInt a1, ClipInt a2, ClipInt b1, ClipInt b2, out ClipInt Left, out ClipInt Right)
        {
            if (a1 < a2)
            {
                if (b1 < b2) { Left = Math.Max(a1, b1); Right = Math.Min(a2, b2); }
                else { Left = Math.Max(a1, b2); Right = Math.Min(a2, b1); }
            }
            else
            {
                if (b1 < b2) { Left = Math.Max(a2, b1); Right = Math.Min(a1, b2); }
                else { Left = Math.Max(a2, b2); Right = Math.Min(a1, b1); }
            }
            return Left < Right;
        }

        //------------------------------------------------------------------------------

        bool JoinHorz(ref OutPt op1, ref OutPt op1b, ref OutPt op2, ref OutPt op2b,
            IntPoint Pt, bool DiscardLeft)
        {
            Direction Dir1 = (op1.Pt.X > op1b.Pt.X ?
                Direction.dRightToLeft : Direction.dLeftToRight);
            Direction Dir2 = (op2.Pt.X > op2b.Pt.X ?
                Direction.dRightToLeft : Direction.dLeftToRight);
            if (Dir1 == Dir2) return false;

            //When DiscardLeft, we want Op1b to be on the Left of Op1, otherwise we
            //want Op1b to be on the Right. (And likewise with Op2 and Op2b.)
            //So, to facilitate this while inserting Op1b and Op2b ...
            //when DiscardLeft, make sure we're AT or RIGHT of Pt before adding Op1b,
            //otherwise make sure we're AT or LEFT of Pt. (Likewise with Op2b.)
            if (Dir1 == Direction.dLeftToRight)
            {
                while (op1.Next.Pt.X <= Pt.X &&
                       op1.Next.Pt.X >= op1.Pt.X && op1.Next.Pt.Y == Pt.Y)
                    op1 = op1.Next;
                if (DiscardLeft && (op1.Pt.X != Pt.X)) op1 = op1.Next;
                DupOutPt(ref op1, !DiscardLeft, out op1b);
                if (op1b.Pt != Pt)
                {
                    op1 = op1b;
                    op1.Pt = Pt;
                    DupOutPt(ref op1, !DiscardLeft, out op1b);
                }
            }
            else
            {
                while (op1.Next.Pt.X >= Pt.X &&
                       op1.Next.Pt.X <= op1.Pt.X && op1.Next.Pt.Y == Pt.Y)
                    op1 = op1.Next;
                if (!DiscardLeft && (op1.Pt.X != Pt.X)) op1 = op1.Next;
                DupOutPt(ref op1, DiscardLeft, out op1b);
                if (op1b.Pt != Pt)
                {
                    op1 = op1b;
                    op1.Pt = Pt;
                    DupOutPt(ref op1, DiscardLeft, out op1b);
                }
            }

            if (Dir2 == Direction.dLeftToRight)
            {
                while (op2.Next.Pt.X <= Pt.X &&
                       op2.Next.Pt.X >= op2.Pt.X && op2.Next.Pt.Y == Pt.Y)
                    op2 = op2.Next;
                if (DiscardLeft && (op2.Pt.X != Pt.X)) op2 = op2.Next;
                DupOutPt(ref op2, !DiscardLeft, out op2b);
                if (op2b.Pt != Pt)
                {
                    op2 = op2b;
                    op2.Pt = Pt;
                    DupOutPt(ref op2, !DiscardLeft, out op2b);
                }
            }
            else
            {
                while (op2.Next.Pt.X >= Pt.X &&
                       op2.Next.Pt.X <= op2.Pt.X && op2.Next.Pt.Y == Pt.Y)
                    op2 = op2.Next;
                if (!DiscardLeft && (op2.Pt.X != Pt.X)) op2 = op2.Next;
                DupOutPt(ref op2, DiscardLeft, out op2b);
                if (op2b.Pt != Pt)
                {
                    op2 = op2b;
                    op2.Pt = Pt;
                    DupOutPt(ref op2, DiscardLeft, out op2b);
                }
            }

            if ((Dir1 == Direction.dLeftToRight) == DiscardLeft)
            {
                op1.Prev = op2;
                op2.Next = op1;
                op1b.Next = op2b;
                op2b.Prev = op1b;
            }
            else
            {
                op1.Next = op2;
                op2.Prev = op1;
                op1b.Prev = op2b;
                op2b.Next = op1b;
            }
            return true;
        }

        //------------------------------------------------------------------------------

        private bool JoinPoints(ref Join j, ref OutRec outRec1, ref OutRec outRec2)
        {
            OutPt op1 = j.OutPt1, op1b;
            OutPt op2 = j.OutPt2, op2b;

            //There are 3 kinds of joins for output polygons ...
            //1. Horizontal joins where Join.OutPt1 & Join.OutPt2 are vertices anywhere
            //along (horizontal) collinear edges (& Join.OffPt is on the same horizontal).
            //2. Non-horizontal joins where Join.OutPt1 & Join.OutPt2 are at the same
            //location at the Bottom of the overlapping segment (& Join.OffPt is above).
            //3. StrictlySimple joins where edges touch but are not collinear and where
            //Join.OutPt1, Join.OutPt2 & Join.OffPt all share the same point.
            bool isHorizontal = (j.OutPt1.Pt.Y == j.OffPt.Y);

            if (isHorizontal && (j.OffPt == j.OutPt1.Pt) && (j.OffPt == j.OutPt2.Pt))
            {
                //Strictly Simple join ...
                if (outRec1 != outRec2) return false;
                op1b = j.OutPt1.Next;
                while (op1b != op1 && (op1b.Pt == j.OffPt))
                    op1b = op1b.Next;
                bool reverse1 = (op1b.Pt.Y > j.OffPt.Y);
                op2b = j.OutPt2.Next;
                while (op2b != op2 && (op2b.Pt == j.OffPt))
                    op2b = op2b.Next;
                bool reverse2 = (op2b.Pt.Y > j.OffPt.Y);
                if (reverse1 == reverse2) return false;
                if (reverse1)
                {
                    DupOutPt(ref op1, false, out op1b);
                    DupOutPt(ref op2, true, out op2b);
                    op1.Prev = op2;
                    op2.Next = op1;
                    op1b.Next = op2b;
                    op2b.Prev = op1b;
                    j.OutPt1 = op1;
                    j.OutPt2 = op1b;
                    return true;
                }
                else
                {
                    DupOutPt(ref op1, true, out op1b);
                    DupOutPt(ref op2, false, out op2b);
                    op1.Next = op2;
                    op2.Prev = op1;
                    op1b.Prev = op2b;
                    op2b.Next = op1b;
                    j.OutPt1 = op1;
                    j.OutPt2 = op1b;
                    return true;
                }
            }
            else if (isHorizontal)
            {
                //treat horizontal joins differently to non-horizontal joins since with
                //them we're not yet sure where the overlapping is. OutPt1.Pt & OutPt2.Pt
                //may be anywhere along the horizontal edge.
                op1b = op1;
                while (op1.Prev.Pt.Y == op1.Pt.Y && op1.Prev != op1b && op1.Prev != op2)
                    op1 = op1.Prev;
                while (op1b.Next.Pt.Y == op1b.Pt.Y && op1b.Next != op1 && op1b.Next != op2)
                    op1b = op1b.Next;
                if (op1b.Next == op1 || op1b.Next == op2) return false; //a flat 'polygon'

                op2b = op2;
                while (op2.Prev.Pt.Y == op2.Pt.Y && op2.Prev != op2b && op2.Prev != op1b)
                    op2 = op2.Prev;
                while (op2b.Next.Pt.Y == op2b.Pt.Y && op2b.Next != op2 && op2b.Next != op1)
                    op2b = op2b.Next;
                if (op2b.Next == op2 || op2b.Next == op1) return false; //a flat 'polygon'

                ClipInt Left, Right;
                //Op1 -. Op1b & Op2 -. Op2b are the extremites of the horizontal edges
                if (!GetOverlap(op1.Pt.X, op1b.Pt.X, op2.Pt.X, op2b.Pt.X, out Left, out Right))
                    return false;

                //DiscardLeftSide: when overlapping edges are joined, a spike will created
                //which needs to be cleaned up. However, we don't want Op1 or Op2 caught up
                //on the discard Side as either may still be needed for other joins ...
                IntPoint Pt;
                bool DiscardLeftSide;
                if (op1.Pt.X >= Left && op1.Pt.X <= Right)
                {
                    Pt = op1.Pt; DiscardLeftSide = (op1.Pt.X > op1b.Pt.X);
                }
                else if (op2.Pt.X >= Left && op2.Pt.X <= Right)
                {
                    Pt = op2.Pt; DiscardLeftSide = (op2.Pt.X > op2b.Pt.X);
                }
                else if (op1b.Pt.X >= Left && op1b.Pt.X <= Right)
                {
                    Pt = op1b.Pt; DiscardLeftSide = op1b.Pt.X > op1.Pt.X;
                }
                else
                {
                    Pt = op2b.Pt; DiscardLeftSide = (op2b.Pt.X > op2.Pt.X);
                }
                j.OutPt1 = op1;
                j.OutPt2 = op2;
                return JoinHorz(ref op1, ref op1b, ref op2, ref op2b, Pt, DiscardLeftSide);
            }
            else
            {
                //nb: For non-horizontal joins ...
                //    1. Jr.OutPt1.Pt.Y == Jr.OutPt2.Pt.Y
                //    2. Jr.OutPt1.Pt > Jr.OffPt.Y

                //make sure the polygons are correctly oriented ...
                op1b = op1.Next;
                while ((op1b.Pt == op1.Pt) && (op1b != op1)) op1b = op1b.Next;
                bool Reverse1 = ((op1b.Pt.Y > op1.Pt.Y) ||
                    !SlopesEqual(op1.Pt, op1b.Pt, j.OffPt, m_UseFullRange));
                if (Reverse1)
                {
                    op1b = op1.Prev;
                    while ((op1b.Pt == op1.Pt) && (op1b != op1)) op1b = op1b.Prev;
                    if ((op1b.Pt.Y > op1.Pt.Y) ||
                        !SlopesEqual(op1.Pt, op1b.Pt, j.OffPt, m_UseFullRange)) return false;
                }
                op2b = op2.Next;
                while ((op2b.Pt == op2.Pt) && (op2b != op2)) op2b = op2b.Next;
                bool Reverse2 = ((op2b.Pt.Y > op2.Pt.Y) ||
                    !SlopesEqual(op2.Pt, op2b.Pt, j.OffPt, m_UseFullRange));
                if (Reverse2)
                {
                    op2b = op2.Prev;
                    while ((op2b.Pt == op2.Pt) && (op2b != op2)) op2b = op2b.Prev;
                    if ((op2b.Pt.Y > op2.Pt.Y) ||
                        !SlopesEqual(op2.Pt, op2b.Pt, j.OffPt, m_UseFullRange)) return false;
                }

                if ((op1b == op1) || (op2b == op2) || (op1b == op2b) ||
                    ((outRec1 == outRec2) && (Reverse1 == Reverse2))) return false;

                if (Reverse1)
                {
                    DupOutPt(ref op1, false, out op1b);
                    DupOutPt(ref op2, true, out op2b);
                    op1.Prev = op2;
                    op2.Next = op1;
                    op1b.Next = op2b;
                    op2b.Prev = op1b;
                    j.OutPt1 = op1;
                    j.OutPt2 = op1b;
                    return true;
                }
                else
                {
                    DupOutPt(ref op1, true, out op1b);
                    DupOutPt(ref op2, false, out op2b);
                    op1.Next = op2;
                    op2.Prev = op1;
                    op1b.Prev = op2b;
                    op2b.Next = op1b;
                    j.OutPt1 = op1;
                    j.OutPt2 = op1b;
                    return true;
                }
            }
        }

        //----------------------------------------------------------------------

        public static int PointInPolygon(IntPoint pt, ref Path path)
        {
            //returns 0 if false, +1 if true, -1 if pt ON polygon boundary
            //See "The Point in Polygon Problem for Arbitrary Polygons" by Hormann & Agathos
            //http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.88.5498&rep=rep1&type=pdf
            int result = 0, cnt = path.Length;
            if (cnt < 3) return 0;
            IntPoint ip = path[0];
            for (int i = 1; i <= cnt; ++i)
            {
                IntPoint ipNext = (i == cnt ? path[0] : path[i]);
                if (ipNext.Y == pt.Y)
                {
                    if ((ipNext.X == pt.X) || (ip.Y == pt.Y &&
                                               ((ipNext.X > pt.X) == (ip.X < pt.X)))) return -1;
                }
                if ((ip.Y < pt.Y) != (ipNext.Y < pt.Y))
                {
                    if (ip.X >= pt.X)
                    {
                        if (ipNext.X > pt.X) result = 1 - result;
                        else
                        {
                            double d = (double)(ip.X - pt.X) * (ipNext.Y - pt.Y) -
                                (double)(ipNext.X - pt.X) * (ip.Y - pt.Y);
                            if (d == 0) return -1;
                            else if ((d > 0) == (ipNext.Y > ip.Y)) result = 1 - result;
                        }
                    }
                    else
                    {
                        if (ipNext.X > pt.X)
                        {
                            double d = (double)(ip.X - pt.X) * (ipNext.Y - pt.Y) -
                                (double)(ipNext.X - pt.X) * (ip.Y - pt.Y);
                            if (d == 0) return -1;
                            else if ((d > 0) == (ipNext.Y > ip.Y)) result = 1 - result;
                        }
                    }
                }
                ip = ipNext;
            }
            return result;
        }

        //------------------------------------------------------------------------------

        //See "The Point in Polygon Problem for Arbitrary Polygons" by Hormann & Agathos
        //http://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.88.5498&rep=rep1&type=pdf
        private static int PointInPolygon(IntPoint pt, ref OutPt op)
        {
            //returns 0 if false, +1 if true, -1 if pt ON polygon boundary
            int result = 0;
            OutPt startOp = op;
            ClipInt ptx = pt.X, pty = pt.Y;
            ClipInt poly0x = op.Pt.X, poly0y = op.Pt.Y;
            do
            {
                op = op.Next;
                ClipInt poly1x = op.Pt.X, poly1y = op.Pt.Y;

                if (poly1y == pty)
                {
                    if ((poly1x == ptx) || (poly0y == pty &&
                                            ((poly1x > ptx) == (poly0x < ptx)))) return -1;
                }
                if ((poly0y < pty) != (poly1y < pty))
                {
                    if (poly0x >= ptx)
                    {
                        if (poly1x > ptx) result = 1 - result;
                        else
                        {
                            double d = (double)(poly0x - ptx) * (poly1y - pty) -
                                (double)(poly1x - ptx) * (poly0y - pty);
                            if (d == 0) return -1;
                            if ((d > 0) == (poly1y > poly0y)) result = 1 - result;
                        }
                    }
                    else
                    {
                        if (poly1x > ptx)
                        {
                            double d = (double)(poly0x - ptx) * (poly1y - pty) -
                                (double)(poly1x - ptx) * (poly0y - pty);
                            if (d == 0) return -1;
                            if ((d > 0) == (poly1y > poly0y)) result = 1 - result;
                        }
                    }
                }
                poly0x = poly1x; poly0y = poly1y;
            }
            while (startOp != op);
            return result;
        }

        //------------------------------------------------------------------------------

        private static bool Poly2ContainsPoly1(ref OutPt outPt1, ref OutPt outPt2)
        {
            OutPt op = outPt1;
            do
            {
                //nb: PointInPolygon returns 0 if false, +1 if true, -1 if pt on polygon
                int res = PointInPolygon(op.Pt, ref outPt2);
                if (res >= 0) return res > 0;
                op = op.Next;
            }
            while (op != outPt1);
            return true;
        }

        //----------------------------------------------------------------------

        private void FixupFirstLefts1(ref OutRec OldOutRec, ref OutRec NewOutRec)
        {
            foreach (OutRec outRec in m_PolyOuts)
            {
                OutRec firstLeft;
                ParseFirstLeft(ref outRec.FirstLeft, out firstLeft);
                if (outRec.Pts.NotNull && firstLeft == OldOutRec)
                {
                    if (Poly2ContainsPoly1(ref outRec.Pts, ref NewOutRec.Pts))
                        outRec.FirstLeft = NewOutRec;
                }
            }
        }

        //----------------------------------------------------------------------

        private void FixupFirstLefts2(ref OutRec innerOutRec, ref OutRec outerOutRec)
        {
            //A polygon has split into two such that one is now the inner of the other.
            //It's possible that these polygons now wrap around other polygons, so check
            //every polygon that's also contained by OuterOutRec's FirstLeft container
            //(including nil) to see if they've become inner to the new inner polygon ...
            OutRec orfl = outerOutRec.FirstLeft;
            foreach (OutRec outRec in m_PolyOuts)
            {
                if (outRec.Pts.IsNull || outRec == outerOutRec || outRec == innerOutRec)
                    continue;
                OutRec firstLeft;
                ParseFirstLeft(ref outRec.FirstLeft, out firstLeft);
                if (firstLeft != orfl && firstLeft != innerOutRec && firstLeft != outerOutRec)
                    continue;
                if (Poly2ContainsPoly1(ref outRec.Pts, ref innerOutRec.Pts))
                    outRec.FirstLeft = innerOutRec;
                else if (Poly2ContainsPoly1(ref outRec.Pts, ref outerOutRec.Pts))
                    outRec.FirstLeft = outerOutRec;
                else if (outRec.FirstLeft == innerOutRec || outRec.FirstLeft == outerOutRec)
                    outRec.FirstLeft = orfl;
            }
        }

        //----------------------------------------------------------------------

        private void FixupFirstLefts3(ref OutRec OldOutRec, ref OutRec NewOutRec)
        {
            //same as FixupFirstLefts1 but doesn't call Poly2ContainsPoly1()
            foreach (OutRec outRec in m_PolyOuts)
            {
                OutRec firstLeft;
                ParseFirstLeft(ref outRec.FirstLeft, out firstLeft);
                if (outRec.Pts.NotNull && firstLeft == OldOutRec)
                    outRec.FirstLeft = NewOutRec;
            }
        }

        //----------------------------------------------------------------------

        private static void ParseFirstLeft(ref OutRec FirstLeft, out OutRec result)
        {
            while (FirstLeft.NotNull && FirstLeft.Pts.IsNull)
                FirstLeft = FirstLeft.FirstLeft;
            result = FirstLeft;
        }

        //------------------------------------------------------------------------------

        private void JoinCommonEdges()
        {
            for (int i = 0; i < m_Joins.Length; i++)
            {
                Join join = m_Joins[i];

                OutRec outRec1;
                GetOutRec(join.OutPt1.Idx, out outRec1);
                OutRec outRec2;
                GetOutRec(join.OutPt2.Idx, out outRec2);

                if (outRec1.Pts.IsNull || outRec2.Pts.IsNull) continue;
                if (outRec1.IsOpen || outRec2.IsOpen) continue;

                //get the polygon fragment with the correct hole state (FirstLeft)
                //before calling JoinPoints() ...
                OutRec holeStateRec;
                if (outRec1 == outRec2) holeStateRec = outRec1;
                else if (OutRec1RightOfOutRec2(ref outRec1, ref outRec2)) holeStateRec = outRec2;
                else if (OutRec1RightOfOutRec2(ref outRec2, ref outRec1)) holeStateRec = outRec1;
                else GetLowermostRec(ref outRec1, ref outRec2, out holeStateRec);

                if (!JoinPoints(ref join, ref outRec1, ref outRec2)) continue;

                if (outRec1 == outRec2)
                {
                    //instead of joining two polygons, we've just created a new one by
                    //splitting one polygon into two.
                    outRec1.Pts = join.OutPt1;
                    outRec1.BottomPt.SetNull();
                    CreateOutRec(out outRec2);
                    outRec2.Pts = join.OutPt2;

                    //update all OutRec2.Pts Idx's ...
                    UpdateOutPtIdxs(ref outRec2);

                    if (Poly2ContainsPoly1(ref outRec2.Pts, ref outRec1.Pts))
                    {
                        //outRec1 contains outRec2 ...
                        outRec2.IsHole = !outRec1.IsHole;
                        outRec2.FirstLeft = outRec1;

                        if (m_UsingPolyTree) FixupFirstLefts2(ref outRec2, ref outRec1);

                        if ((outRec2.IsHole ^ ReverseSolution) == (Area(ref outRec2) > 0))
                            ReversePolyPtLinks(ref outRec2.Pts);
                    }
                    else if (Poly2ContainsPoly1(ref outRec1.Pts, ref outRec2.Pts))
                    {
                        //outRec2 contains outRec1 ...
                        outRec2.IsHole = outRec1.IsHole;
                        outRec1.IsHole = !outRec2.IsHole;
                        outRec2.FirstLeft = outRec1.FirstLeft;
                        outRec1.FirstLeft = outRec2;

                        if (m_UsingPolyTree) FixupFirstLefts2(ref outRec1, ref outRec2);

                        if ((outRec1.IsHole ^ ReverseSolution) == (Area(ref outRec1) > 0))
                            ReversePolyPtLinks(ref outRec1.Pts);
                    }
                    else
                    {
                        //the 2 polygons are completely separate ...
                        outRec2.IsHole = outRec1.IsHole;
                        outRec2.FirstLeft = outRec1.FirstLeft;

                        //fixup FirstLeft pointers that may need reassigning to OutRec2
                        if (m_UsingPolyTree) FixupFirstLefts1(ref outRec1, ref outRec2);
                    }
                }
                else
                {
                    //joined 2 polygons together ...

                    outRec2.Pts.SetNull();
                    outRec2.BottomPt.SetNull();
                    outRec2.Idx = outRec1.Idx;

                    outRec1.IsHole = holeStateRec.IsHole;
                    if (holeStateRec == outRec2)
                        outRec1.FirstLeft = outRec2.FirstLeft;
                    outRec2.FirstLeft = outRec1;

                    //fixup FirstLeft pointers that may need reassigning to OutRec1
                    if (m_UsingPolyTree) FixupFirstLefts3(ref outRec2, ref outRec1);
                }
            }
        }

        //------------------------------------------------------------------------------

        private void UpdateOutPtIdxs(ref OutRec outrec)
        {
            OutPt op = outrec.Pts;
            do
            {
                op.Idx = outrec.Idx;
                op = op.Prev;
            }
            while (op != outrec.Pts);
        }

        //------------------------------------------------------------------------------

        private void DoSimplePolygons()
        {
            int i = 0;
            while (i < m_PolyOuts.Length)
            {
                OutRec outrec = m_PolyOuts[i++];
                OutPt op = outrec.Pts;
                if (op.IsNull || outrec.IsOpen) continue;
                do //for each Pt in Polygon until duplicate found do ...
                {
                    OutPt op2 = op.Next;
                    while (op2 != outrec.Pts)
                    {
                        if ((op.Pt == op2.Pt) && op2.Next != op && op2.Prev != op)
                        {
                            //split the polygon into two ...
                            OutPt op3 = op.Prev;
                            OutPt op4 = op2.Prev;
                            op.Prev = op4;
                            op4.Next = op;
                            op2.Prev = op3;
                            op3.Next = op2;

                            outrec.Pts = op;
                            OutRec outrec2;
                            CreateOutRec(out outrec2);
                            outrec2.Pts = op2;
                            UpdateOutPtIdxs(ref outrec2);
                            if (Poly2ContainsPoly1(ref outrec2.Pts, ref outrec.Pts))
                            {
                                //OutRec2 is contained by OutRec1 ...
                                outrec2.IsHole = !outrec.IsHole;
                                outrec2.FirstLeft = outrec;
                                if (m_UsingPolyTree) FixupFirstLefts2(ref outrec2, ref outrec);
                            }
                            else if (Poly2ContainsPoly1(ref outrec.Pts, ref outrec2.Pts))
                            {
                                //OutRec1 is contained by OutRec2 ...
                                outrec2.IsHole = outrec.IsHole;
                                outrec.IsHole = !outrec2.IsHole;
                                outrec2.FirstLeft = outrec.FirstLeft;
                                outrec.FirstLeft = outrec2;
                                if (m_UsingPolyTree) FixupFirstLefts2(ref outrec, ref outrec2);
                            }
                            else
                            {
                                //the 2 polygons are separate ...
                                outrec2.IsHole = outrec.IsHole;
                                outrec2.FirstLeft = outrec.FirstLeft;
                                if (m_UsingPolyTree) FixupFirstLefts1(ref outrec, ref outrec2);
                            }
                            op2 = op; //ie get ready for the next iteration
                        }
                        op2 = op2.Next;
                    }
                    op = op.Next;
                }
                while (op != outrec.Pts);
            }
        }

        //------------------------------------------------------------------------------

        public static double Area(ref Path poly)
        {
            int cnt = (int)poly.Length;
            if (cnt < 3) return 0;
            double a = 0;
            for (int i = 0, j = cnt - 1; i < cnt; ++i)
            {
                a += ((double)poly[j].X + poly[i].X) * ((double)poly[j].Y - poly[i].Y);
                j = i;
            }
            return -a * 0.5;
        }

        //------------------------------------------------------------------------------

        internal double Area(ref OutRec outRec)
        {
            return Area(ref outRec.Pts);
        }

        //------------------------------------------------------------------------------

        internal double Area(ref OutPt op)
        {
            OutPt opFirst = op;
            if (op.IsNull) return 0;
            double a = 0;
            do
            {
                a = a + (double)(op.Prev.Pt.X + op.Pt.X) * (double)(op.Prev.Pt.Y - op.Pt.Y);
                op = op.Next;
            }
            while (op != opFirst);
            return a * 0.5;
        }

        //------------------------------------------------------------------------------
        // SimplifyPolygon functions ...
        // Convert self-intersecting polygons into simple polygons
        //------------------------------------------------------------------------------

        public static void SimplifyPolygon(ref Path poly, out Paths result,
            PolyFillType fillType = PolyFillType.pftEvenOdd)
        {
            result = new Paths(1, Allocator.Temp, NativeArrayOptions.ClearMemory);
            Clipper c = new Clipper();
            c.Initialize();
            c.StrictlySimple = true;
            c.AddPath(ref poly, PolyType.ptSubject, true);
            c.Execute(ClipType.ctUnion, ref result, fillType, fillType);
        }

        //------------------------------------------------------------------------------

        public static void SimplifyPolygons(ref Paths polys, out Paths result,
            PolyFillType fillType = PolyFillType.pftEvenOdd)
        {
            result = new Paths(1, Allocator.Temp, NativeArrayOptions.ClearMemory);
            Clipper c = new Clipper();
            c.Initialize();
            c.StrictlySimple = true;
            c.AddPaths(ref polys, PolyType.ptSubject, true);
            c.Execute(ClipType.ctUnion, ref result, fillType, fillType);
        }

        //------------------------------------------------------------------------------

        private static double DistanceSqrd(IntPoint pt1, IntPoint pt2)
        {
            double dx = ((double)pt1.X - pt2.X);
            double dy = ((double)pt1.Y - pt2.Y);
            return (dx * dx + dy * dy);
        }

        //------------------------------------------------------------------------------

        private static double DistanceFromLineSqrd(IntPoint pt, IntPoint ln1, IntPoint ln2)
        {
            //The equation of a line in general form (Ax + By + C = 0)
            //given 2 points (xÂ¹,yÂ¹) & (xÂ²,yÂ²) is ...
            //(yÂ¹ - yÂ²)x + (xÂ² - xÂ¹)y + (yÂ² - yÂ¹)xÂ¹ - (xÂ² - xÂ¹)yÂ¹ = 0
            //A = (yÂ¹ - yÂ²); B = (xÂ² - xÂ¹); C = (yÂ² - yÂ¹)xÂ¹ - (xÂ² - xÂ¹)yÂ¹
            //perpendicular distance of point (xÂ³,yÂ³) = (AxÂ³ + ByÂ³ + C)/Sqrt(AÂ² + BÂ²)
            //see http://en.wikipedia.org/wiki/Perpendicular_distance
            double A = ln1.Y - ln2.Y;
            double B = ln2.X - ln1.X;
            double C = A * ln1.X + B * ln1.Y;
            C = A * pt.X + B * pt.Y - C;
            return (C * C) / (A * A + B * B);
        }

        //---------------------------------------------------------------------------

        private static bool SlopesNearCollinear(IntPoint pt1,
            IntPoint pt2, IntPoint pt3, double distSqrd)
        {
            //this function is more accurate when the point that's GEOMETRICALLY
            //between the other 2 points is the one that's tested for distance.
            //nb: with 'spikes', either pt1 or pt3 is geometrically between the other pts
            if (Math.Abs(pt1.X - pt2.X) > Math.Abs(pt1.Y - pt2.Y))
            {
                if ((pt1.X > pt2.X) == (pt1.X < pt3.X))
                    return DistanceFromLineSqrd(pt1, pt2, pt3) < distSqrd;
                else if ((pt2.X > pt1.X) == (pt2.X < pt3.X))
                    return DistanceFromLineSqrd(pt2, pt1, pt3) < distSqrd;
                else
                    return DistanceFromLineSqrd(pt3, pt1, pt2) < distSqrd;
            }
            else
            {
                if ((pt1.Y > pt2.Y) == (pt1.Y < pt3.Y))
                    return DistanceFromLineSqrd(pt1, pt2, pt3) < distSqrd;
                else if ((pt2.Y > pt1.Y) == (pt2.Y < pt3.Y))
                    return DistanceFromLineSqrd(pt2, pt1, pt3) < distSqrd;
                else
                    return DistanceFromLineSqrd(pt3, pt1, pt2) < distSqrd;
            }
        }

        //------------------------------------------------------------------------------

        private static bool PointsAreClose(IntPoint pt1, IntPoint pt2, double distSqrd)
        {
            double dx = (double)pt1.X - pt2.X;
            double dy = (double)pt1.Y - pt2.Y;
            return ((dx * dx) + (dy * dy) <= distSqrd);
        }

        //------------------------------------------------------------------------------

        private static void ExcludeOp(ref OutPt op, out OutPt result)
        {
            result = op.Prev;
            result.Next = op.Next;
            op.Next.Prev = result;
            result.Idx = 0;
        }

        //------------------------------------------------------------------------------

        public static void CleanPolygon(ref Path path, out Path cleanPath, double distance = 1.415)
        {
            //distance = proximity in units/pixels below which vertices will be stripped.
            //Default ~= sqrt(2) so when adjacent vertices or semi-adjacent vertices have
            //both x & y coords within 1 unit, then the second vertex will be stripped.

            int cnt = path.Length;

            if (cnt == 0)
            {
                cleanPath = new Path(1, Allocator.Temp, NativeArrayOptions.ClearMemory);
                return;
            }

            OutPt[] outPts = new OutPt[cnt];
            for (int i = 0; i < cnt; ++i)
            {
                outPts[i] = new OutPt();
                outPts[i].Initialize();
            }

            for (int i = 0; i < cnt; ++i)
            {
                outPts[i].Pt = path[i];
                outPts[i].Next = outPts[(i + 1) % cnt];
                outPts[i].Next.Prev = outPts[i];
                outPts[i].Idx = 0;
            }

            double distSqrd = distance * distance;
            OutPt op = outPts[0];
            while (op.Idx == 0 && op.Next != op.Prev)
            {
                if (PointsAreClose(op.Pt, op.Prev.Pt, distSqrd))
                {
                    ExcludeOp(ref op, out op);
                    cnt--;
                }
                else if (PointsAreClose(op.Prev.Pt, op.Next.Pt, distSqrd))
                {
                    OutPt unused;
                    ExcludeOp(ref op.Next, out unused);
                    ExcludeOp(ref op, out op);
                    cnt -= 2;
                }
                else if (SlopesNearCollinear(op.Prev.Pt, op.Pt, op.Next.Pt, distSqrd))
                {
                    ExcludeOp(ref op, out op);
                    cnt--;
                }
                else
                {
                    op.Idx = 1;
                    op = op.Next;
                }
            }

            if (cnt < 3) cnt = 0;
            Path result = new Path(cnt, Allocator.Temp, NativeArrayOptions.ClearMemory);
            for (int i = 0; i < cnt; ++i)
            {
                result.Add(op.Pt);
                op = op.Next;
            }
            outPts = null;

            cleanPath = result;
        }

        //------------------------------------------------------------------------------

        public static void CleanPolygons(ref Paths polys, out Paths result,
            double distance = 1.415)
        {
            result = new Paths(polys.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);
            for (int i = 0; i < polys.Length; i++)
            {
                Path cleanPath;
                CleanPolygon(ref polys.GetIndexByRef(i), out cleanPath, distance);
                result.Add(cleanPath);
            }
        }

        //------------------------------------------------------------------------------

        internal static void Minkowski(ref Path pattern, ref Path path, bool IsSum, bool IsClosed, out Paths result)
        {
            int delta = (IsClosed ? 1 : 0);
            int polyCnt = pattern.Length;
            int pathCnt = path.Length;

            result = new Paths(pathCnt, Allocator.Temp, NativeArrayOptions.ClearMemory);
            if (IsSum)
                for (int i = 0; i < pathCnt; i++)
                {
                    Path p = new Path(polyCnt, Allocator.Temp, NativeArrayOptions.ClearMemory);
                    for(int patternIndex=0; patternIndex < pattern.Length; patternIndex++)
                    {
                        IntPoint ip = pattern[patternIndex];
                        p.Add(new IntPoint(path[i].X + ip.X, path[i].Y + ip.Y));
                    }
                    result.Add(p);
                }
            else
                for (int i = 0; i < pathCnt; i++)
                {
                    Path p = new Path(polyCnt, Allocator.Temp, NativeArrayOptions.ClearMemory);
                    for (int patternIndex = 0; patternIndex < pattern.Length; patternIndex++)
                    {
                        IntPoint ip = pattern[patternIndex];
                        p.Add(new IntPoint(path[i].X - ip.X, path[i].Y - ip.Y));
                    }
                    result.Add(p);
                }

            Paths quads = new Paths((pathCnt + delta) * (polyCnt + 1), Allocator.Temp, NativeArrayOptions.ClearMemory);
            for (int i = 0; i < pathCnt - 1 + delta; i++)
                for (int j = 0; j < polyCnt; j++)
                {
                    Path quad = new Path(4, Allocator.Temp, NativeArrayOptions.ClearMemory);
                    quad.Add(result[i % pathCnt][j % polyCnt]);
                    quad.Add(result[(i + 1) % pathCnt][j % polyCnt]);
                    quad.Add(result[(i + 1) % pathCnt][(j + 1) % polyCnt]);
                    quad.Add(result[i % pathCnt][(j + 1) % polyCnt]);
                    if (!Orientation(ref quad)) quad.Reverse();
                    quads.Add(quad);
                }
        }

        //------------------------------------------------------------------------------

        public static void MinkowskiSum(ref Path pattern, ref Path path, bool pathIsClosed, out Paths paths)
        {
            Minkowski(ref pattern, ref path, true, pathIsClosed, out paths);
            Clipper c = new Clipper();
            c.Initialize();
            c.AddPaths(ref paths, PolyType.ptSubject, true);
            c.Execute(ClipType.ctUnion, ref paths, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
        }

        //------------------------------------------------------------------------------

        private static void TranslatePath(ref Path path, ref IntPoint delta, out Path outPath)
        {
            outPath = new Path(path.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);
            for (int i = 0; i < path.Length; i++)
                outPath.Add(new IntPoint(path[i].X + delta.X, path[i].Y + delta.Y));
        }

        //------------------------------------------------------------------------------

        public static void MinkowskiSum(ref Path pattern, ref Paths paths, bool pathIsClosed, out Paths solution)
        {
            solution = new Paths(1, Allocator.Temp, NativeArrayOptions.ClearMemory);
            Clipper c = new Clipper();
            c.Initialize();
            for (int i = 0; i < paths.Length; ++i)
            {
                Paths tmp;
                Minkowski(ref pattern, ref paths.GetIndexByRef(i), true, pathIsClosed, out tmp);
                c.AddPaths(ref tmp, PolyType.ptSubject, true);
                if (pathIsClosed)
                {
                    Path translatedPath;
                    TranslatePath(ref paths.GetIndexByRef(i), ref pattern.GetIndexByRef(0), out translatedPath);
                    c.AddPath(ref translatedPath, PolyType.ptClip, true);
                }
            }
            c.Execute(ClipType.ctUnion, ref solution,
                PolyFillType.pftNonZero, PolyFillType.pftNonZero);
        }

        //------------------------------------------------------------------------------

        public static void MinkowskiDiff(ref Path poly1, ref Path poly2, out Paths paths)
        {
            Minkowski(ref poly1, ref poly2, false, true, out paths);
            Clipper c = new Clipper();
            c.Initialize();
            c.AddPaths(ref paths, PolyType.ptSubject, true);
            c.Execute(ClipType.ctUnion, ref paths, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
        }

        //------------------------------------------------------------------------------

        internal enum NodeType { ntAny, ntOpen, ntClosed };

        //------------------------------------------------------------------------------

        internal static void AddPolyNodeToPaths(ref PolyNode polynode, ref NodeType nt, ref Paths paths)
        {
            bool match = true;
            switch (nt)
            {
                case NodeType.ntOpen: return;
                case NodeType.ntClosed: match = !polynode.IsOpen; break;
                default: break;
            }

            if (polynode.m_polygon.Length > 0 && match)
                paths.Add(polynode.m_polygon);

            for(int i=0;i<polynode.Childs.Length;i++)
            {
                PolyNode pn = polynode.Childs[i];
                AddPolyNodeToPaths(ref pn, ref nt, ref paths);
            }
        }

        //------------------------------------------------------------------------------
    } //end Clipper

    internal struct ClipperOffset
    {
        private Paths m_destPolys;
        private Path m_srcPoly;
        private Path m_destPoly;
        private UnsafeList<DoublePoint> m_normals;
        private double m_delta, m_sinA, m_sin, m_cos;
        private double m_StepsPerRad;

        private IntPoint m_lowest;
        private PolyNode m_polyNodes;

        public double ArcTolerance { get; set; }

        private const double two_pi = Math.PI * 2;
        private const double def_arc_tolerance = 0.25;

        public ClipperOffset(double arcTolerance = def_arc_tolerance)
        {
            m_destPolys = new Paths(1, Allocator.Temp, NativeArrayOptions.ClearMemory);
            m_srcPoly = new Path(1, Allocator.Temp, NativeArrayOptions.ClearMemory);
            m_destPoly = new Path(1, Allocator.Temp, NativeArrayOptions.ClearMemory);
            m_normals = new UnsafeList<DoublePoint>(1, Allocator.Temp, NativeArrayOptions.ClearMemory);
            m_polyNodes = new PolyNode();
            m_polyNodes.Initialize();

            m_delta = 0;
            m_sinA = 0;
            m_sin = 0;
            m_cos = 0;
            m_StepsPerRad = 0;

            ArcTolerance = arcTolerance;
            m_lowest = new IntPoint(-1, 0);
        }

        //------------------------------------------------------------------------------

        public void Clear()
        {
            m_polyNodes.Childs.Clear();
            m_lowest.X = -1;
        }

        //------------------------------------------------------------------------------

        internal static ClipInt Round(double value)
        {
            return value < 0 ? (ClipInt)(value - 0.5) : (ClipInt)(value + 0.5);
        }

        //------------------------------------------------------------------------------

        public void AddPath(ref Path path, JoinType joinType, EndType endType)
        {
            int highI = path.Length - 1;
            if (highI < 0) return;
            PolyNode newNode = new PolyNode();
            newNode.Initialize();

            newNode.m_jointype = joinType;
            newNode.m_endtype = endType;

            //strip duplicate points from path and also get index to the lowest point ...
            if (endType == EndType.etClosedLine || endType == EndType.etClosedPolygon)
                while (highI > 0 && path[0] == path[highI]) highI--;
            newNode.m_polygon.Capacity = highI + 1;
            newNode.m_polygon.Add(path[0]);
            int j = 0, k = 0;
            for (int i = 1; i <= highI; i++)
                if (newNode.m_polygon[j] != path[i])
                {
                    j++;
                    newNode.m_polygon.Add(path[i]);
                    if (path[i].Y > newNode.m_polygon[k].Y ||
                        (path[i].Y == newNode.m_polygon[k].Y &&
                         path[i].X < newNode.m_polygon[k].X)) k = j;
                }
            if (endType == EndType.etClosedPolygon && j < 2) return;

            m_polyNodes.AddChild(newNode);

            //if this path's lowest pt is lower than all the others then update m_lowest
            if (endType != EndType.etClosedPolygon) return;
            if (m_lowest.X < 0)
                m_lowest = new IntPoint(m_polyNodes.ChildCount - 1, k);
            else
            {
                IntPoint ip = m_polyNodes.Childs[(int)m_lowest.X].m_polygon[(int)m_lowest.Y];
                if (newNode.m_polygon[k].Y > ip.Y ||
                    (newNode.m_polygon[k].Y == ip.Y &&
                     newNode.m_polygon[k].X < ip.X))
                    m_lowest = new IntPoint(m_polyNodes.ChildCount - 1, k);
            }
        }

        //------------------------------------------------------------------------------

        public void AddPaths(ref Paths paths, JoinType joinType, EndType endType)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                Path p = paths[i];
                AddPath(ref p, joinType, endType);
            }
        }

        //------------------------------------------------------------------------------

        private void FixOrientations()
        {
            Path polygon = m_polyNodes.Childs[(int)m_lowest.X].m_polygon;
            //fixup orientations of all closed paths if the orientation of the
            //closed path with the lowermost vertex is wrong ...
            if (m_lowest.X >= 0 &&
                !Clipper.Orientation(ref polygon))
            {
                for (int i = 0; i < m_polyNodes.ChildCount; i++)
                {
                    PolyNode node = m_polyNodes.Childs[i];
                    if (node.m_endtype == EndType.etClosedPolygon ||
                        (node.m_endtype == EndType.etClosedLine &&
                         Clipper.Orientation(ref node.m_polygon)))
                        node.m_polygon.Reverse();
                }
            }
            else
            {
                for (int i = 0; i < m_polyNodes.ChildCount; i++)
                {
                    PolyNode node = m_polyNodes.Childs[i];
                    if (node.m_endtype == EndType.etClosedLine &&
                        !Clipper.Orientation(ref node.m_polygon))
                        node.m_polygon.Reverse();
                }
            }
        }

        //------------------------------------------------------------------------------

        internal static DoublePoint GetUnitNormal(IntPoint pt1, IntPoint pt2)
        {
            double dx = (pt2.X - pt1.X);
            double dy = (pt2.Y - pt1.Y);
            if ((dx == 0) && (dy == 0)) return new DoublePoint();

            double f = 1 * 1.0 / Math.Sqrt(dx * dx + dy * dy);
            dx *= f;
            dy *= f;

            return new DoublePoint(dy, -dx);
        }

        //------------------------------------------------------------------------------

        private void DoOffset(double delta)
        {
            m_destPolys = new Paths(1, Allocator.Temp, NativeArrayOptions.ClearMemory);
            m_delta = delta;

            //if Zero offset, just copy any CLOSED polygons to m_p and return ...
            if (Clipper.near_zero(delta))
            {
                m_destPolys.Capacity = m_polyNodes.ChildCount;
                for (int i = 0; i < m_polyNodes.ChildCount; i++)
                {
                    PolyNode node = m_polyNodes.Childs[i];
                    if (node.m_endtype == EndType.etClosedPolygon)
                        m_destPolys.Add(node.m_polygon);
                }
                return;
            }

            double y;
            if (ArcTolerance <= 0.0)
                y = def_arc_tolerance;
            else if (ArcTolerance > Math.Abs(delta) * def_arc_tolerance)
                y = Math.Abs(delta) * def_arc_tolerance;
            else
                y = ArcTolerance;
            //see offset_triginometry2.svg in the documentation folder ...
            double steps = Math.PI / Math.Acos(1 - y / Math.Abs(delta));
            m_sin = Math.Sin(two_pi / steps);
            m_cos = Math.Cos(two_pi / steps);
            m_StepsPerRad = steps / two_pi;
            if (delta < 0.0) m_sin = -m_sin;

            m_destPolys.Capacity = m_polyNodes.ChildCount * 2;
            for (int i = 0; i < m_polyNodes.ChildCount; i++)
            {
                PolyNode node = m_polyNodes.Childs[i];
                m_srcPoly = node.m_polygon;

                int len = m_srcPoly.Length;

                if (len == 0 || (delta <= 0 && (len < 3 ||
                                                node.m_endtype != EndType.etClosedPolygon)))
                    continue;

                m_destPoly = new Path(1, Allocator.Temp, NativeArrayOptions.ClearMemory);

                if (len == 1)
                {
                    if (node.m_jointype == JoinType.jtRound)
                    {
                        double X = 1.0, Y = 0.0;
                        for (int j = 1; j <= steps; j++)
                        {
                            m_destPoly.Add(new IntPoint(
                                Round(m_srcPoly[0].X + X * delta),
                                Round(m_srcPoly[0].Y + Y * delta)));
                            double X2 = X;
                            X = X * m_cos - m_sin * Y;
                            Y = X2 * m_sin + Y * m_cos;
                        }
                    }
                    else
                    {
                        double X = -1.0, Y = -1.0;
                        for (int j = 0; j < 4; ++j)
                        {
                            m_destPoly.Add(new IntPoint(
                                Round(m_srcPoly[0].X + X * delta),
                                Round(m_srcPoly[0].Y + Y * delta)));
                            if (X < 0) X = 1;
                            else if (Y < 0) Y = 1;
                            else X = -1;
                        }
                    }
                    m_destPolys.Add(m_destPoly);
                    continue;
                }

                //build m_normals ...
                m_normals.Clear();
                m_normals.Capacity = len;
                for (int j = 0; j < len - 1; j++)
                    m_normals.Add(GetUnitNormal(m_srcPoly[j], m_srcPoly[j + 1]));
                if (node.m_endtype == EndType.etClosedLine ||
                    node.m_endtype == EndType.etClosedPolygon)
                    m_normals.Add(GetUnitNormal(m_srcPoly[len - 1], m_srcPoly[0]));
                else
                    m_normals.Add(new DoublePoint(m_normals[len - 2]));

                if (node.m_endtype == EndType.etClosedPolygon)
                {
                    int k = len - 1;
                    for (int j = 0; j < len; j++)
                        OffsetPoint(j, ref k, node.m_jointype);
                    m_destPolys.Add(m_destPoly);
                }
                else if (node.m_endtype == EndType.etClosedLine)
                {
                    int k = len - 1;
                    for (int j = 0; j < len; j++)
                        OffsetPoint(j, ref k, node.m_jointype);
                    m_destPolys.Add(m_destPoly);
                    m_destPoly = new Path(1, Allocator.Temp, NativeArrayOptions.ClearMemory);
                    //re-build m_normals ...
                    DoublePoint n = m_normals[len - 1];
                    for (int j = len - 1; j > 0; j--)
                        m_normals[j] = new DoublePoint(-m_normals[j - 1].X, -m_normals[j - 1].Y);
                    m_normals[0] = new DoublePoint(-n.X, -n.Y);
                    k = 0;
                    for (int j = len - 1; j >= 0; j--)
                        OffsetPoint(j, ref k, node.m_jointype);
                    m_destPolys.Add(m_destPoly);
                }
                else
                {
                    int k = 0;
                    for (int j = 1; j < len - 1; ++j)
                        OffsetPoint(j, ref k, node.m_jointype);

                    {
                        int j = len - 1;
                        k = len - 2;
                        m_sinA = 0;
                        m_normals[j] = new DoublePoint(-m_normals[j].X, -m_normals[j].Y);
                        DoRound(j, k);
                    }

                    //re-build m_normals ...
                    for (int j = len - 1; j > 0; j--)
                        m_normals[j] = new DoublePoint(-m_normals[j - 1].X, -m_normals[j - 1].Y);

                    m_normals[0] = new DoublePoint(-m_normals[1].X, -m_normals[1].Y);

                    k = len - 1;
                    for (int j = k - 1; j > 0; --j)
                        OffsetPoint(j, ref k, node.m_jointype);

                    {
                        k = 1;
                        m_sinA = 0;
                        DoRound(0, 1);
                    }
                    m_destPolys.Add(m_destPoly);
                }
            }
        }

        //------------------------------------------------------------------------------

        public void Execute(ref Paths solution, double delta, int inputSize)
        {
            solution.Clear();
            FixOrientations();
            DoOffset(delta);
            //now clean up 'corners' ...
            Clipper clpr = new Clipper();
            clpr.Initialize();
            clpr.AddPaths(ref m_destPolys, PolyType.ptSubject, true);
            clpr.LastIndex = inputSize - 1;
            if (delta > 0)
            {
                clpr.Execute(ClipType.ctUnion, ref solution,
                    PolyFillType.pftPositive, PolyFillType.pftPositive);
            }
            else
            {
                IntRect r = Clipper.GetBounds(ref m_destPolys);
                Path outer = new Path(4, Allocator.Temp, NativeArrayOptions.ClearMemory);

                outer.Add(new IntPoint(r.left - 10, r.bottom + 10));
                outer.Add(new IntPoint(r.right + 10, r.bottom + 10));
                outer.Add(new IntPoint(r.right + 10, r.top - 10));
                outer.Add(new IntPoint(r.left - 10, r.top - 10));

                clpr.AddPath(ref outer, PolyType.ptSubject, true);
                clpr.ReverseSolution = true;
                clpr.Execute(ClipType.ctUnion, ref solution, PolyFillType.pftNegative, PolyFillType.pftNegative);
                if (solution.Length > 0) solution.RemoveAt(0);
            }
        }

        //------------------------------------------------------------------------------

        public void Execute(ref Paths solution, double delta)
        {
            solution.Clear();
            FixOrientations();
            DoOffset(delta);
            //now clean up 'corners' ...
            Clipper clpr = new Clipper();
            clpr.Initialize();
            clpr.AddPaths(ref m_destPolys, PolyType.ptSubject, true);
            if (delta > 0)
            {
                clpr.Execute(ClipType.ctUnion, ref solution,
                  PolyFillType.pftPositive, PolyFillType.pftPositive);
            }
            else
            {
                IntRect r = Clipper.GetBounds(ref m_destPolys);
                Path outer = new Path(4, Allocator.Temp, NativeArrayOptions.ClearMemory);

                outer.Add(new IntPoint(r.left - 10, r.bottom + 10));
                outer.Add(new IntPoint(r.right + 10, r.bottom + 10));
                outer.Add(new IntPoint(r.right + 10, r.top - 10));
                outer.Add(new IntPoint(r.left - 10, r.top - 10));

                clpr.AddPath(ref outer, PolyType.ptSubject, true);
                clpr.ReverseSolution = true;
                clpr.Execute(ClipType.ctUnion, ref solution, PolyFillType.pftNegative, PolyFillType.pftNegative);
                if (solution.Length > 0) solution.RemoveAt(0);
            }
        }

        //------------------------------------------------------------------------------

        void OffsetPoint(int j, ref int k, JoinType jointype)
        {
            //cross product ...
            m_sinA = (m_normals[k].X * m_normals[j].Y - m_normals[j].X * m_normals[k].Y);

            if (Math.Abs(m_sinA * m_delta) < 1.0)
            {
                //dot product ...
                double cosA = (m_normals[k].X * m_normals[j].X + m_normals[j].Y * m_normals[k].Y);
                if (cosA > 0) // angle ==> 0 degrees
                {
                    var item = new IntPoint(Round(m_srcPoly[j].X + m_normals[k].X * m_delta),
                        Round(m_srcPoly[j].Y + m_normals[k].Y * m_delta));
                    item.NX = m_normals[k].X; item.NY = m_normals[k].Y; item.N = j; item.D = 1;
                    m_destPoly.Add(item);
                    return;
                }
                //else angle ==> 180 degrees
            }
            else if (m_sinA > 1.0) m_sinA = 1.0;
            else if (m_sinA < -1.0) m_sinA = -1.0;

            if (m_sinA * m_delta < 0)
            {
                var pt = new IntPoint(Round(m_srcPoly[j].X + m_normals[k].X * m_delta),
                    Round(m_srcPoly[j].Y + m_normals[k].Y * m_delta));
                pt.NX = m_normals[k].X; pt.NY = m_normals[k].Y;
                m_destPoly.Add(pt);
                pt = m_srcPoly[j];
                pt.NX = m_normals[k].X; pt.NY = m_normals[k].Y; pt.N = j; pt.D = 1;
                m_destPoly.Add(pt);
                pt = new IntPoint(Round(m_srcPoly[j].X + m_normals[j].X * m_delta),
                    Round(m_srcPoly[j].Y + m_normals[j].Y * m_delta));
                pt.NX = m_normals[j].X; pt.NY = m_normals[j].Y; pt.N = j; pt.D = 1;
                m_destPoly.Add(pt);
            }
            else
                switch (jointype)
                {
                    case JoinType.jtRound: DoRound(j, k); break;
                }
            k = j;
        }

        //------------------------------------------------------------------------------

        internal void DoSquare(int j, int k)
        {
            double dx = Math.Tan(Math.Atan2(m_sinA,
                m_normals[k].X * m_normals[j].X + m_normals[k].Y * m_normals[j].Y) / 4);
            var pt = new IntPoint(
                Round(m_srcPoly[j].X + m_delta * (m_normals[k].X - m_normals[k].Y * dx)),
                Round(m_srcPoly[j].Y + m_delta * (m_normals[k].Y + m_normals[k].X * dx)));
            pt.NX = m_normals[k].X - m_normals[k].Y * dx; pt.NY = m_normals[k].Y + m_normals[k].X * dx;
            m_destPoly.Add(pt);
            pt = new IntPoint(
                Round(m_srcPoly[j].X + m_delta * (m_normals[j].X + m_normals[j].Y * dx)),
                Round(m_srcPoly[j].Y + m_delta * (m_normals[j].Y - m_normals[j].X * dx)));
            pt.NX = m_normals[k].X + m_normals[k].Y * dx; pt.NY = m_normals[k].Y - m_normals[k].X * dx;
            m_destPoly.Add(pt);
        }

        //------------------------------------------------------------------------------

        internal void DoMiter(int j, int k, double r)
        {
            double q = m_delta / r;
            var pt = new IntPoint(Round(m_srcPoly[j].X + (m_normals[k].X + m_normals[j].X) * q),
                Round(m_srcPoly[j].Y + (m_normals[k].Y + m_normals[j].Y) * q));
            pt.NX = (m_normals[k].X + m_normals[j].X) * q; pt.NY = (m_normals[k].Y + m_normals[j].Y) * q;
            m_destPoly.Add(pt);
        }

        //------------------------------------------------------------------------------

        internal void DoRound(int j, int k)
        {
            double a = Math.Atan2(m_sinA,
                m_normals[k].X * m_normals[j].X + m_normals[k].Y * m_normals[j].Y);
            int steps = Math.Max((int)Round(m_StepsPerRad * Math.Abs(a)), 1);

            double X = m_normals[k].X, Y = m_normals[k].Y, X2;
            for (int i = 0; i < steps; ++i)
            {
                var pt = new IntPoint(
                    Round(m_srcPoly[j].X + X * m_delta),
                    Round(m_srcPoly[j].Y + Y * m_delta));
                pt.NX = X; pt.NY = Y; pt.N = j; pt.D = 1;
                m_destPoly.Add(pt);
                X2 = X;
                X = X * m_cos - m_sin * Y;
                Y = X2 * m_sin + Y * m_cos;
            }

            var pt1 = new IntPoint(
                Round(m_srcPoly[j].X + m_normals[j].X * m_delta),
                Round(m_srcPoly[j].Y + m_normals[j].Y * m_delta));
            pt1.NX = m_normals[j].X; pt1.NY = m_normals[j].Y; pt1.N = j; pt1.D = 1;
            m_destPoly.Add(pt1);
        }

        //------------------------------------------------------------------------------
    }

    class ClipperException : Exception
    {
        public ClipperException(string description) : base(description) { }
    }
    //------------------------------------------------------------------------------
} //end ClipperLib namespace
