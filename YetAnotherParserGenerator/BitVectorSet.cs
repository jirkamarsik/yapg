using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YetAnotherParserGenerator.Utilities
{
    /// <summary>
    /// A collection representing a set of integers ranging from 0..k for some predetermined k.
    /// </summary>
    public class BitVectorSet : IEnumerable<int>
    {
        private static int intBits = sizeof(int) * 8;
        private static int[] bitMasks;

        private int capacity;
        private int[] bitVectors;

        //statický konstruktor nám připraví bitové masky pro pohodlnější přístup do bitových vektorů
        static BitVectorSet()
        {
            bitMasks = new int[32];
            bitMasks[0] = 1;
            for (int i = 1; i <= 31; i++)
                bitMasks[i] = bitMasks[i - 1] << 1;
        }

        /// <summary>
        /// Creates an empty BitVectorSet with the specified range length.
        /// </summary>
        /// <param name="capacity">The length of the range of all possible BitVectorSet elements.</param>
        public BitVectorSet(int capacity)
        {
            this.capacity = capacity;

            int vectors = capacity / intBits;
            if (capacity % intBits > 0)
                vectors++;

            bitVectors = new int[vectors];
        }

        /// <summary>
        /// Creates a BitVectorSet by duplicating another one.
        /// </summary>
        /// <param name="originalSet">The BitVectorSet to be duplicated.</param>
        public BitVectorSet(BitVectorSet originalSet)
        {
            this.capacity = originalSet.capacity;
            this.bitVectors = new int[originalSet.bitVectors.Length];
            originalSet.bitVectors.CopyTo(this.bitVectors, 0);
        }

        /// <summary>
        /// Adds an integer to the collection if not already present.
        /// </summary>
        /// <param name="element">An integer in the BitVectorSet's allowed range to be added to the set.</param>
        /// <exception cref="ArgumentOutOfRangeException">when the integer is outside the range of values stored in the BitVectorSet.</exception>
        public void Add(int element)
        {
            if ((element < 0) || (element >= capacity))
                throw new ArgumentOutOfRangeException("element", "The specified integer was outside of the range supported by the BitVectorSet.");
            bitVectors[element / intBits] |= bitMasks[element % intBits];
        }

        /// <summary>
        /// Removes an integer from the collection if present.
        /// </summary>
        /// <param name="element">An integer in the BitVectorSet's allowed range to be removed from the set.</param>
        /// <exception cref="ArgumentOutOfRangeException">when the integer is outside the range of values stored in the BitVectorSet.</exception>
        public void Remove(int element)
        {
            if ((element < 0) || (element >= capacity))
                throw new ArgumentOutOfRangeException("element", "The specified integer was outside of the range supported by the BitVectorSet.");
            bitVectors[element / intBits] &= ~bitMasks[element % intBits];
        }

        /// <summary>
        /// Check wheter an integer is contained in the BitVectorSet.
        /// </summary>
        /// <param name="element">An integer in the BitVectorSet's allowed range.</param>
        /// <exception cref="ArgumentOutOfRangeException">when the integer is outside the range of values stored in the BitVectorSet.</exception>
        public bool Contains(int element)
        {
            if ((element < 0) || (element >= capacity))
                throw new ArgumentOutOfRangeException("element", "The specified integer was outside of the range supported by the BitVectorSet.");
            return (bitVectors[element / intBits] & bitMasks[element % intBits]) != 0;
        }

        /// <summary>
        /// Computes the union of the elements contained in this BitVectorSet and the <i>otherSet</i>
        /// and stores the result in this BitVectorSet instance.
        /// </summary>
        /// <param name="otherSet">The set with which this instance's set is to be unioned with.</param>
        /// <exception cref="ArgumentException">when the BitVectorSets are geared for different ranges.</exception>
        public void UnionWith(BitVectorSet otherSet)
        {
            if (this.capacity != otherSet.capacity)
                throw new ArgumentException("The sets are geared for different ranges.", "otherSet");
            for (int i = 0; i < this.bitVectors.Length; i++)
                this.bitVectors[i] |= otherSet.bitVectors[i];
        }

        /// <summary>
        /// Returns a new BitVectorSet which is the union of this BitVectorSet and the <i>otherSet</i>.
        /// </summary>
        /// <param name="otherSet">The set whose union with this set is to be computed.</param>
        /// <returns>The union of this set and the <i>otherSet</i>.</returns>
        public BitVectorSet GetUnionWith(BitVectorSet otherSet)
        {
            BitVectorSet union = new BitVectorSet(this);
            union.UnionWith(otherSet);

            return union;
        }

        /// <summary>
        /// Computes the intersection of the elements contained in this BitVectorSet and the <i>otherSet</i>
        /// and stores the result in this BitVectorSet instance.
        /// </summary>
        /// <param name="otherSet">The set with which this instance's set is to be intersected with.</param>
        /// <exception cref="ArgumentException">when the BitVectorSets are geared for different ranges.</exception>
        public void IntersectWith(BitVectorSet otherSet)
        {
            if (this.capacity != otherSet.capacity)
                throw new ArgumentException("The sets are geared for different ranges.", "otherSet");
            for (int i = 0; i < this.bitVectors.Length; i++)
                this.bitVectors[i] &= otherSet.bitVectors[i];
        }

        /// <summary>
        /// Returns a new BitVectorSet which is the intersection of this BitVectorSet and the <i>otherSet</i>.
        /// </summary>
        /// <param name="otherSet">The set whose intersection with this set is to be computed.</param>
        /// <returns>The intersection of this set and the <i>otherSet</i>.</returns>
        public BitVectorSet GetIntersectionWith(BitVectorSet otherSet)
        {
            BitVectorSet intersection = new BitVectorSet(this);
            intersection.IntersectWith(otherSet);

            return intersection;
        }

        /// <summary>
        /// Determines whether the two sets represented by this BitVectorSet instance and the <i>otherSet</i>
        /// are mutually disjoint.
        /// </summary>
        /// <param name="otherSet">The set to be checked for intersections with this instance.</param>
        /// <returns><b>true</b> if the two sets are disjoint; <b>false</b> otherwise</returns>
        /// <exception cref="ArgumentException">when the BitVectorSets are geared for different ranges.</exception>
        public bool IsDisjointWith(BitVectorSet otherSet)
        {
            if (this.capacity != otherSet.capacity)
                throw new ArgumentException("The sets are geared for different capacities.", "otherSet");
            for (int i = 0; i < this.bitVectors.Length; i++ )
                if ((this.bitVectors[i] & otherSet.bitVectors[i]) != 0)
                    return false;
            return true;
        }


        /// <summary>
        /// Computes the relative complement of <i>set2</i> in <i>set1</i>.
        /// </summary>
        /// <returns>The relative complement of <i>set2</i> in <i>set1</i>.</returns>
        public static BitVectorSet operator -(BitVectorSet set1, BitVectorSet set2)
        {
            BitVectorSet complement = new BitVectorSet(set1.capacity);

            if (set1.capacity != set2.capacity)
                throw new ArgumentException("The sets are geared for different capacities.", "otherSet");
            for (int i = 0; i < complement.bitVectors.Length; i++)
                complement.bitVectors[i] = set1.bitVectors[i] & ~set2.bitVectors[i];

            return complement;
        }

        /// <summary>
        /// Gets whether the BitVectorSet is empty.
        /// </summary>
        /// <returns><b>true</b> if the BitVectorSet is empty; <b>false</b> otherwise</returns>
        public bool IsEmpty()
        {
            for (int i = 0; i < this.bitVectors.Length; i++)
                if (bitVectors[i] > 0)
                    return false;
            return true;
        }

        /// <summary>
        /// Returns an IEnumerator&lt;int&gt; which iterates over the elements of this collection.
        /// </summary>
        /// <returns></returns>
        public IEnumerator<int> GetEnumerator()
        {
            int element = 0;
            while (element < capacity)
            {
                if ((bitVectors[element / intBits] & bitMasks[element % intBits]) != 0)
                    yield return element;
                element++;
            }
        }

        /// <summary>
        /// Returns an IEnumerator which iterates over the elements of this collection.
        /// </summary>
        /// <returns></returns>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            int element = 0;
            while (element < capacity)
            {
                if ((bitVectors[element / intBits] & bitMasks[element % intBits]) != 0)
                    yield return element;
                element++;
            }
        }
    }
}
