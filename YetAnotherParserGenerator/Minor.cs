using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YetAnotherParserGenerator
{
    /// <summary>
    /// Represents an occurence of a terminal symbol found by a lexer.
    /// </summary>
    public struct Token
    {
        /// <summary>
        /// The code of the terminal symbol as identified by the lexer.
        /// </summary>
        public int SymbolCode;

        /// <summary>
        /// The text assigned by the lexer to this terminal symbol.
        /// </summary>
        public string Value;

        /// <summary>
        /// The line at which the text of this terminal symbol begins.
        /// </summary>
        public int LineNumber;

        /// <summary>
        /// The position within the line at which the text of this terminal symbol begins.
        /// </summary>
        public int ColumnNumber;
    }

    /// <summary>
    /// Represents the different types of actions one can find in an LR automaton's parse table.
    /// </summary>
    [Serializable]
    public enum ParserActionType
    {
        /// <summary>
        /// The parser has no way to carry on parsing.
        /// </summary>
        Fail,
        /// <summary>
        /// The parser decides to read the next symbol from the input buffer and push a state on the stack.
        /// </summary>
        Shift,
        /// <summary>
        /// The parser decides to reduce symbols from the stack according to a production.
        /// </summary>
        Reduce,
    }

    /// <summary>
    /// Represents a parser's action in a given situation. Used as a unit of the LR automaton's parse table.
    /// </summary>
    [Serializable]
    public struct ParserAction
    {
        /// <summary>
        /// The type of the action.
        /// </summary>
        public ParserActionType ActionType;

        /// <summary>
        /// If ActionType == ParserActionType.Shift, this specifies the state which is to be pushed on the stack.
        /// If ActionType == ParserActionType.Reduce, this specifies according to which production should symbols
        /// on the stack be reduced.
        /// If ActionType == ParserActionType.Fail, this value has no meaning.
        /// </summary>
        public int Argument;
    }

    /// <summary>
    /// Represents one of the productions of a grammar.
    /// </summary>
    public class Production
    {
        private int productionCode;
        private int lhsSymbol;
        private List<int> rhsSymbols;
        
        /// <summary>
        /// Creates a production with an empty right-hand side.
        /// </summary>
        /// <param name="productionCode">The ordinal code of the production.</param>
        /// <param name="lhsSymbol">The left-hand side symbol's code.</param>
        public Production(int productionCode, int lhsSymbol)
        {
            this.productionCode = productionCode;
            this.lhsSymbol = lhsSymbol;
            this.rhsSymbols = new List<int>();
        }

        /// <summary>
        /// Creates a complete from the specified parameters.
        /// </summary>
        /// <param name="productionCode">The ordinal code of the production.</param>
        /// <param name="lhsSymbol">The left-hand side symbol's code.</param>
        /// <param name="rhsSymbols">The collection whose elements will form the right-hand side of the production.</param>
        public Production(int productionCode, int lhsSymbol, IEnumerable<int> rhsSymbols)
        {
            this.productionCode = productionCode;
            this.lhsSymbol = lhsSymbol;
            this.rhsSymbols = new List<int>(rhsSymbols);
        }

        /// <summary>
        /// Gets the ordinal code of the production.
        /// </summary>
        public int ProductionCode { get { return productionCode; } set { productionCode = value; } }

        /// <summary>
        /// Gets the left-hand side symbol's code.
        /// </summary>
        public int LHSSymbol { get { return lhsSymbol; } }

        /// <summary>
        /// Gets the right-hand side symbols' codes of the production.
        /// </summary>
        public List<int> RHSSymbols { get { return rhsSymbols; } }
    }
    
	public class ProductionWithAction {
		public ProductionWithAction(Production production, string action)
		{
			this.Production = production;
			this.Action = action;
		}
		
		public Production Production;
		public string Action;
	}

    /// <summary>
    /// A stripped down representation of a production containing only the information by the parser.
    /// </summary>
    [Serializable]
    public class ProductionOutline
    {
        private int lhsSymbol;
        private int numRhsSymbols;
        
        /// <summary>
        /// Creates a new instance of ProductionOutline with all needed information.
        /// </summary>
        /// <param name="lhsSymbol">The left-hand side symbol's code.</param>
        /// <param name="numRhsSymbols">The number of symbols on the right-hand side of the production.</param>
        public ProductionOutline(int lhsSymbol, int numRhsSymbols)
        {
            this.lhsSymbol = lhsSymbol;
            this.numRhsSymbols = numRhsSymbols;
        }

        /// <summary>
        /// Gets the left-hand side symbol's code.
        /// </summary>
        public int LHSSymbol { get { return lhsSymbol; } }

        /// <summary>
        /// Gets the number of symbols on the right-hand side of the productions.
        /// </summary>
        public int NumRHSSymbols { get { return numRhsSymbols; } }
    }

    /// <summary>
    /// Represents a state of the LR automaton.
    /// </summary>
    public class State
    {
        private int stateNumber;
        private ItemSet itemSet;
        private List<Transition> transitions = new List<Transition>();
        private List<State> accessingStates = new List<State>();
        
        /// <summary>
        /// Creates a new State with the supplied number and ItemSet.
        /// </summary>
        /// <param name="stateNumber">The number of the state.</param>
        /// <param name="itemSet">The state's defining ItemSet.</param>
        public State(int stateNumber, ItemSet itemSet)
        {
            this.stateNumber = stateNumber;
            this.itemSet = itemSet;
        }

        /// <summary>
        /// Gets the number of the state.
        /// </summary>
        public int StateNumber { get { return stateNumber; } }

        /// <summary>
        /// Gets the ItemSet defining this state.
        /// </summary>
        public ItemSet ItemSet { get { return itemSet; } }

        /// <summary>
        /// Gets the collection of transitions leading from this state.
        /// </summary>
        public List<Transition> Transitions { get { return transitions; } }

        /// <summary>
        /// Gets the collection of states accessing this state.
        /// </summary>
        public List<State> AccessingStates { get { return accessingStates; } }
    }

    /// <summary>
    /// Represents a transition in the the LR automaton's state graph.
    /// </summary>
    public abstract class Transition
    {
        protected int transitionSymbol;
        protected State source;
        protected State destination;
        
        /// <summary>
        /// Gets the transition symbol's code.
        /// </summary>
        public int TransitionSymbol { get { return transitionSymbol; } }

        /// <summary>
        /// Gets the State where this transition originates.
        /// </summary>
        public State Source { get { return source; } }

        /// <summary>
        /// Gets the State to which this transition leads.
        /// </summary>
        public State Destination { get { return destination; } }
    }

    /// <summary>
    /// Represents a terminal transition in the LR automaton's state graph.
    /// </summary>
    public class TerminalTransition : Transition
    {
        /// <summary>
        /// Creates a new TerminalTransition from <i>source</i> to <i>destination</i> under <i>transitionSymbol</i>.
        /// The transition still has to be added to the Transitions of <i>source</i> and <i>source</i> should
        /// be added to AccessingStates of <i>destination</i>.
        /// </summary>
        /// <param name="source">The State where the transition originates.</param>
        /// <param name="destination">The State to which the transition leads.</param>
        /// <param name="transitionSymbol">The transition symbol's code.</param>
        public TerminalTransition(State source, State destination, int transitionSymbol)
        {
            this.source = source;
            this.destination = destination;
            this.transitionSymbol = transitionSymbol;
        }
    }

    /// <summary>
    /// Represents a nonterminal transition in the LR automaton's state graph.
    /// </summary>
    public class NonterminalTransition : Transition
    {
        private int nonterminalTransitionNumber;
        
        /// <summary>
        /// Creates a new NonterminalTransition from <i>source</i> to <i>destination</i> under <i>transitionSymbol</i>.
        /// The transition still has to be added to the Transitions of <i>source</i> and <i>source</i> should
        /// be added to AccessingStates of <i>destination</i>.
        /// </summary>
        /// <param name="source">The State where the transition originates.</param>
        /// <param name="destination">The State to which the transition leads.</param>
        /// <param name="transitionSymbol">The transition symbol's code.</param>
        /// <param name="nonterminalTransitionNumber">The ordinal number of the nonterminal transition.</param>
        public NonterminalTransition(State source, State destination, int transitionSymbol, int nonterminalTransitionNumber)
        {
            this.source = source;
            this.destination = destination;
            this.transitionSymbol = transitionSymbol;
            this.nonterminalTransitionNumber = nonterminalTransitionNumber;
        }

        /// <summary>
        /// Gets the ordinal number of this nonterminal transition.
        /// </summary>
        public int NonterminalTransitionNumber { get { return nonterminalTransitionNumber; } }
    }
}
