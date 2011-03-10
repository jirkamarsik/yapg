using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YetAnotherParserGenerator
{
    /// <summary>
    /// Represents an item, in the LR automaton sense, a position within a grammar's production.
    /// </summary>
    public struct Item
    {
        /// <summary>
        /// Creates a new Item from a Production and a position in it's right-hand side symbols.
        /// </summary>
        /// <param name="production">The Production referenced by the item.</param>
        /// <param name="position">The zero-based position in the <i>production</i>'s right-hand side symbols.</param>
        public Item(Production production, int position)
        {
            this.production = production;
            this.position = position;
        }

        /// <summary>
        /// Gets the Production referenced by the item.
        /// </summary>
        public Production Production { get { return production; } }
        private Production production;

        /// <summary>
        /// Gets the zero-based position in the right-hand side of the item's production.
        /// </summary>
        public int Position { get { return position; } }
        private int position;

        /// <summary>
        /// Gets whether the item is a final one.
        /// </summary>
        public bool IsFinal
        { get { return position == this.Production.RHSSymbols.Count; } }

        /// <summary>
        /// Determines whether the specified Item is equal to the current Item.
        /// </summary>
        /// <param name="obj">The Item which is to be compared with current one.</param>
        /// <returns><b>true</b> if the items are equal; <b>false</b> otherwise</returns>
        public override bool Equals(object obj)
        {
            if (!(obj is Item))
                return false;
            else
            {
                Item otherItem = (Item)obj;
                return (this.Production == otherItem.Production) && (this.Position == otherItem.Position);
            }
        }

        public static bool operator ==(Item itemA, Item itemB)
        {
            return itemA.Equals(itemB);
        }

        public static bool operator !=(Item itemA, Item itemB)
        {
            return !itemA.Equals(itemB);
        }

        /// <summary>
        /// Serves as a hash function for Items.
        /// </summary>
        /// <returns>The hash code of this item.</returns>
        public override int GetHashCode()
        {
            return this.Production.GetHashCode() + this.Position.GetHashCode() * 37;
        }
    }

    /// <summary>
    /// A set of items with useful functions for item sets.
    /// </summary>
    public class ItemSet : HashSet<Item>
    {
        /// <summary>
        /// Creates an empty ItemSet using the default equality comparer on Items.
        /// </summary>
        public ItemSet()
            : base()
        {
        }

        /// <summary>
        /// Creates an ItemSet containing the specified item using the default equality comparer on Items.
        /// </summary>
        /// <param name="items">The collection of Items which are to be the initial contents of the ItemSet constructed.</param>
        public ItemSet(IEnumerable<Item> items)
            : base(items)
        {
        }

        /// <summary>
        /// Closes the ItemSet by repeatedly adding items for items headed with a nonterminal.
        /// </summary>
        /// <param name="grammar">The grammar according to which is the ItemSet supposed to be closed.</param>
        public void CloseItemSet(Grammar grammar)
        {
            //Itemy se naskládají na zásobník a postupně se z něj budou odebírat.
            //Itemy ze zásobníku se postupně zpracovávají: item do ItemSetu přispěje novými itemy,
            //které se neuloží pouze do ItemSetu, ale i na zásobník, aby mohly plodit další itemy.
            Stack<Item> stack = new Stack<Item>(this);

            while (stack.Count > 0)
            {
                Item item = stack.Pop();

                if (!item.IsFinal &&
                    item.Production.RHSSymbols[item.Position] >= grammar.GrammarDefinition.NumTerminals)
                {
                    int nonterminalIndex = item.Production.RHSSymbols[item.Position] - grammar.GrammarDefinition.NumTerminals;
                    for (int prodIndex = grammar.NonterminalProductionOffset[nonterminalIndex];
                         prodIndex < grammar.NonterminalProductionOffset[nonterminalIndex + 1]; prodIndex++)
                    {
                        Item newItem = new Item(grammar.Productions[prodIndex], 0);
                        if (!this.Contains(newItem))
                        {
                            this.Add(newItem);
                            stack.Push(newItem);
                        } // if
                    } // for
                } // if
            } //while
        } // CloseItemSet

        /// <summary>
        /// Gets the nucleus of the item set reached by proceeding over a specified symbol.
        /// </summary>
        /// <param name="symbol">The symbol of the transition.</param>
        /// <returns>The nucleus of the reached item set.</returns>
        public ItemSet NucleusAfterTransition(int symbol)
        {
            ItemSet nucleusAfterTransition = new ItemSet();

            foreach (Item item in this)
                if (!item.IsFinal && item.Production.RHSSymbols[item.Position] == symbol)
                    nucleusAfterTransition.Add(new Item(item.Production, item.Position + 1));

            return nucleusAfterTransition;
        }
    }
}
