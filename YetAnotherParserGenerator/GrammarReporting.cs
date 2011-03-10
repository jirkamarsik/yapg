using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YetAnotherParserGenerator.Utilities;

namespace YetAnotherParserGenerator
{
    public partial class GrammarProcessor
    {
        private void printAutomatonStates(string logfileName)
        {
            TextWriter writer = new StreamWriter(logfileName);

            writer.WriteLine("<html>");
            writer.WriteLine("<body>");

            printStatesHTML(writer);

            writer.WriteLine("</body>");
            writer.WriteLine("</html>");

            writer.Flush();
            writer.Close();
        }

        private void printStatesHTML(TextWriter writer)
        {
            writer.WriteLine("<h2>Parser states</h2>");

            if (numInconsistentStates > 0)
            {
                writer.Write("<b>States with inconsistencies:</b> ");

                StringBuilder inconsistentStatesString = new StringBuilder();
                foreach (State state in parserStates)
                    if (stateResolvedAt[state.StateNumber] == LookaheadComplexity.Unresolved)
                        inconsistentStatesString.AppendFormat("<a href=\"#State{0}\">{0}</a>, ", state.StateNumber);
                inconsistentStatesString.Remove(inconsistentStatesString.Length - 2, 2);

                writer.WriteLine(inconsistentStatesString.ToString());
            }

            foreach (State state in parserStates)
            {
                writer.Write("<a name=\"State{0}\" id=\"State{0}\"/>", state.StateNumber);
                if (stateResolvedAt[state.StateNumber] == LookaheadComplexity.Unresolved)
                    writer.WriteLine("<h3 style=\"color:red\">State {0}</h3>", state.StateNumber);
                else
                    writer.WriteLine("<h3>State {0}</h3>", state.StateNumber);

                writer.WriteLine("<b>Items:</b>");
                writer.WriteLine("<ul>");

                BitVectorSet conflictSymbols = new BitVectorSet(grammar.NumTerminals);

                if (stateResolvedAt[state.StateNumber] == LookaheadComplexity.Unresolved)
                {
                    //najde symboly, pro které je tento stav nedeterministický, úpravou algoritmu na hledání
                    //konfliktů použitém v metodě checkForConflicts
                    BitVectorSet accumulator = new BitVectorSet(grammar.NumTerminals);

                    foreach (Transition trans in state.Transitions)
                        if (trans is TerminalTransition)
                            accumulator.Add(trans.TransitionSymbol);

                    foreach (BitVectorSet lookaheadSet in lookaheadSets[stateLookaheadIndex[state.StateNumber]])
                    {
                        BitVectorSet newConflictSymbols = accumulator.GetIntersectionWith(lookaheadSet);
                        accumulator.UnionWith(lookaheadSet);
                        conflictSymbols.UnionWith(newConflictSymbols);
                    }
                }

                if (stateResolvedAt[state.StateNumber] == LookaheadComplexity.LR0)
                {
                    //žádný lookahead
                    foreach (Item item in state.ItemSet)
                    {
                        writer.Write("<li>");
                        printItemHTML(item, conflictSymbols, writer);
                        writer.WriteLine("</li>");
                    }
                }
                else
                {
                    //nejdřív vypíšeme finální itemy a jejich lookahead množiny; pro každý konfliktní symbol
                    //v lookahead množině navíc vypíšeme cestu, jakou se do lookahead množiny dostal
                    for (int i = 0; i < conflictingItems[stateLookaheadIndex[state.StateNumber]].Count; i++)
                    {
                        writer.Write("<li>");
                        printItemHTML(conflictingItems[stateLookaheadIndex[state.StateNumber]][i], conflictSymbols, writer);

                        writer.WriteLine("<br>");
                        printLookahead(lookaheadSets[stateLookaheadIndex[state.StateNumber]][i], conflictSymbols, writer);

                        if (!conflictSymbols.IsDisjointWith(lookaheadSets[stateLookaheadIndex[state.StateNumber]][i]))
                        {
                            writer.WriteLine("<br>");
                            printSymbolExplanations(state, conflictingItems[stateLookaheadIndex[state.StateNumber]][i],
                                conflictSymbols.GetIntersectionWith(lookaheadSets[stateLookaheadIndex[state.StateNumber]][i]), writer);
                        }

                        writer.WriteLine("</li>");
                    }

                    //pak vypíšeme zbylé itemy
                    foreach (Item item in state.ItemSet)
                        if (!item.IsFinal)
                        {
                            writer.Write("<li>");
                            printItemHTML(item, conflictSymbols, writer);
                            writer.WriteLine("</li>");
                        }
                }

                writer.WriteLine("</ul>");

                writer.Write("<b>Accessing states:</b> ");

                StringBuilder accessingStatesString = new StringBuilder();
                foreach (State accessingState in state.AccessingStates)
                    accessingStatesString.AppendFormat("<a href=\"#State{0}\">{0}</a>, ", accessingState.StateNumber);
                if (accessingStatesString.Length > 0)
                    accessingStatesString.Remove(accessingStatesString.Length - 2, 2);

                writer.Write(accessingStatesString.ToString());
                writer.WriteLine("<br>");

                writer.Write("<b>Transitions:</b> ");

                StringBuilder transitionsString = new StringBuilder();
                foreach (Transition trans in state.Transitions)
                    transitionsString.AppendFormat("<a href=\"#State{0}\">{0}</a>({1}), ",
                                    trans.Destination.StateNumber, getSymbolPrintName(trans.TransitionSymbol));
                if (transitionsString.Length > 0)
                    transitionsString.Remove(transitionsString.Length - 2, 2);
                
                writer.Write(transitionsString.ToString());
                writer.WriteLine("<br>");
            }

        }

        private string getSymbolPrintName(int symbolCode)
        {
            if (symbolCode < grammar.NumTerminals)
                return grammar.SymbolNames[symbolCode];
            else
                return "&lt;" + grammar.SymbolNames[symbolCode] + "&gt;";
        }

        private void printItemHTML(Item item, BitVectorSet conflictSymbols, TextWriter writer)
        {
            writer.Write(getSymbolPrintName(item.Production.LHSSymbol));
            writer.Write(" ::=");

            for (int i = 0; i < item.Production.RHSSymbols.Count; i++)
            {
                if (i == item.Position)
                {
                    writer.Write(" ·");
                    if ((item.Production.RHSSymbols[i] < grammar.NumTerminals) &&
                         conflictSymbols.Contains(item.Production.RHSSymbols[i]))
                        writer.Write(" <span style=\"color:red\">{0}</span>", getSymbolPrintName(item.Production.RHSSymbols[i]));
                    else
                        writer.Write(" " + getSymbolPrintName(item.Production.RHSSymbols[i]));
                }
                else
                    writer.Write(" " + getSymbolPrintName(item.Production.RHSSymbols[i]));
            }

            if (item.IsFinal)
                writer.Write(" ·");
        }

        private void printLookahead(BitVectorSet lookaheadSet, BitVectorSet conflictSymbols, TextWriter writer)
        {
            StringBuilder lookaheadSetString = new StringBuilder();
            lookaheadSetString.Append('{');
            foreach (int lookaheadSymbol in lookaheadSet)
            {
                if (conflictSymbols.Contains(lookaheadSymbol))
                    lookaheadSetString.AppendFormat("<span style=\"color:red\">{0}</span>", getSymbolPrintName(lookaheadSymbol));
                else
                    lookaheadSetString.Append(getSymbolPrintName(lookaheadSymbol));
                lookaheadSetString.Append(", ");
            }
            lookaheadSetString.Remove(lookaheadSetString.Length - 2, 2);
            lookaheadSetString.Append('}');

            writer.Write(lookaheadSetString.ToString());
        }

        private void printSymbolExplanations(State state, Item finalItem, BitVectorSet conflictSymbols, TextWriter writer)
        {
            //Vyrazíme ze všech vrcholů (vrcholy jsou tady neterminální hrany automatu)
            //ležících v lookback(state, finalItem), které mají ve follow množině nějaký konfliktní symbol.
            //Prohledáváním do hloubkyv grafu relace 'includes' najdeme nejbližší vrcholy, které mají v Read
            //množinách dohromady všechny hledané konfliktní symboly. Posléze vyrazíme z těchto nalezených vrcholů,
            //tentokrát po hranách relace 'reads' a budeme hledat nejbližší vrcholy, které mají v Direct Read množinách
            //dohromady všechny hledané symboly. Z posledně nalezených vrcholů už jsme vždy schopni vystopovat
            //cestu zpět přes reads, includes a lookback hrany až k původnímu konfliktnímu itemu.

            //pole předků a značky u navštívených vrcholů pro oba grafy
            NonterminalTransition[] includesPredecessors = new NonterminalTransition[numNonterminalTransitions];
            bool[] includesExplored = new bool[numNonterminalTransitions];
            NonterminalTransition[] readsPredecessors = new NonterminalTransition[numNonterminalTransitions];
            bool[] readsExplored = new bool[numNonterminalTransitions];

            BitVectorSet symbolsLeftToExplain = new BitVectorSet(conflictSymbols);
            
            //BFS fronty pro oba průchody obou grafů
            Queue<NonterminalTransition> includesTransitions = new Queue<NonterminalTransition>();
            Queue<NonterminalTransition> readsTransitions = new Queue<NonterminalTransition>();

            //vrcholy, které mají v DR množinách konfliktní symboly
            List<NonterminalTransition> rootTransitions = new List<NonterminalTransition>();

            //procházení po lookback hranách
            foreach (NonterminalTransition trans in lookback(state, finalItem))
            {
                if (!follow[getTransNumber(trans)].IsDisjointWith(conflictSymbols))
                {
                    includesTransitions.Enqueue(trans);
                    includesExplored[getTransNumber(trans)] = true;
                }
            }

            //průchod přes includes hrany
            while ((includesTransitions.Count > 0) && (!symbolsLeftToExplain.IsEmpty()))
            {
                NonterminalTransition trans = includesTransitions.Dequeue();

                BitVectorSet readSet = read[getTransNumber(trans)];
                if (!readSet.IsDisjointWith(symbolsLeftToExplain))
                {
                    BitVectorSet symbolsJustExplained = readSet.GetIntersectionWith(symbolsLeftToExplain);
                    symbolsLeftToExplain -= symbolsJustExplained;
                    readsTransitions.Enqueue(trans);
                    readsExplored[getTransNumber(trans)] = true;
                }

                foreach (NonterminalTransition next in includes.GetNeighboursFor(trans))
                    if (!includesExplored[getTransNumber(next)])
                    {
                        includesPredecessors[getTransNumber(next)] = trans;
                        includesExplored[getTransNumber(next)] = true;
                        includesTransitions.Enqueue(next);
                    }
            }

            //reset hledaných symbolů a průchod přes reads hrany
            symbolsLeftToExplain = new BitVectorSet(conflictSymbols);

            while ((readsTransitions.Count > 0) && (!symbolsLeftToExplain.IsEmpty()))
            {
                NonterminalTransition trans = readsTransitions.Dequeue();

                BitVectorSet DRSet = initDR(trans);
                if (!DRSet.IsDisjointWith(symbolsLeftToExplain))
                {
                    BitVectorSet symbolsJustExplained = DRSet.GetIntersectionWith(symbolsLeftToExplain);
                    symbolsLeftToExplain -= symbolsJustExplained;
                    rootTransitions.Add(trans);
                }

                foreach (NonterminalTransition next in reads.GetNeighboursFor(trans))
                    if (!readsExplored[getTransNumber(next)])
                    {
                        readsPredecessors[getTransNumber(next)] = trans;
                        readsExplored[getTransNumber(next)] = true;
                        readsTransitions.Enqueue(next);
                    }
            }

            //teď už jen vystopujeme všechny potřebné cesty z neterminální hrany, která obsahovala konfliktní
            //symbol ve své DR množině, až k itemu, kde tímto symbolem přispěla a způsobila konflikt
            foreach (NonterminalTransition root in rootTransitions)
            {
                Stack<NonterminalTransition> readsPath = new Stack<NonterminalTransition>();
                Stack<NonterminalTransition> includesPath = new Stack<NonterminalTransition>();

                NonterminalTransition trans = root;
                while (readsPredecessors[getTransNumber(trans)] != null)
                {
                    readsPath.Push(trans);
                    trans = readsPredecessors[getTransNumber(trans)];
                }

                while (includesPredecessors[getTransNumber(trans)] != null)
                {
                    includesPath.Push(trans);
                    trans = includesPredecessors[getTransNumber(trans)];
                }



                writer.Write("({0}, ", state.StateNumber);
                printItemHTML(finalItem, conflictSymbols, writer);
                writer.Write(") <b><i>lookback</i></b> (<a href=\"#State{0}\">{0}</a>, {1})", trans.Source.StateNumber,
                                                                getSymbolPrintName(trans.TransitionSymbol));

                foreach (NonterminalTransition transition in includesPath)
                    writer.Write(" <b><i>includes</i></b> (<a href=\"#State{0}\">{0}</a>, {1})", transition.Source.StateNumber,
                                                                getSymbolPrintName(transition.TransitionSymbol));

                foreach (NonterminalTransition transition in readsPath)
                    writer.Write(" <b><i>reads</i></b> (<a href=\"#State{0}\">{0}</a>, {1})", transition.Source.StateNumber,
                                                                getSymbolPrintName(transition.TransitionSymbol));

                writer.Write(" and {");

                StringBuilder explainedSymbolsString = new StringBuilder();
                foreach (int explainedSymbol in initDR(root).GetIntersectionWith(conflictSymbols))
                    explainedSymbolsString.AppendFormat("<span style=\"color:red\">{0}</span>, ", getSymbolPrintName(explainedSymbol));
                explainedSymbolsString.Remove(explainedSymbolsString.Length - 2, 2);

                writer.Write(explainedSymbolsString.ToString());
                writer.Write("} ⊂ <b>DR</b>(" + root.Source.StateNumber.ToString() + ", " + getSymbolPrintName(root.TransitionSymbol) + ")");

                writer.WriteLine("<br>");
            }
        }

        private void printShiftReduceConflicts(TextWriter reportOutput)
        {
            foreach (State state in parserStates)
                if (stateResolvedAt[state.StateNumber] == LookaheadComplexity.Unresolved)
                {
                    BitVectorSet shiftSymbols = new BitVectorSet(grammar.NumTerminals);
                    foreach (Transition trans in state.Transitions)
                        if (trans is TerminalTransition)
                            shiftSymbols.Add(trans.TransitionSymbol);

                    BitVectorSet reduceSymbols = new BitVectorSet(grammar.NumTerminals);
                    foreach (BitVectorSet lookaheadSet in lookaheadSets[stateLookaheadIndex[state.StateNumber]])
                        reduceSymbols.UnionWith(lookaheadSet);

                    foreach (int terminal in shiftSymbols.GetIntersectionWith(reduceSymbols))
                        reportOutput.WriteLine("Warning: shift/reduce conflict in state {0} on symbol '{1}'.",
                            state.StateNumber, grammar.SymbolNames[terminal]);
                }
        }

        private void printSuccessReport(TextWriter reportOutput)
        {
            int numLR0states = 0, numSLR1states = 0, numLALR1states = 0, numConflictStates = 0;

            foreach (State state in parserStates)
                switch (stateResolvedAt[state.StateNumber])
                {
                    case LookaheadComplexity.LR0:
                        numLR0states++;
                        break;
                    case LookaheadComplexity.SLR1:
                        numSLR1states++;
                        break;
                    case LookaheadComplexity.LALR1:
                        numLALR1states++;
                        break;
                    case LookaheadComplexity.Unresolved:
                        numConflictStates++;
                        break;
                }

            reportOutput.WriteLine("Successfully created a parser for the supplied grammar.");
            reportOutput.Write("{0} of the states were resolved via SLR(1) lookaheads and {1} state(s) via LALR(1) lookaheads. ",
                numSLR1states, numLALR1states);
            reportOutput.WriteLine("{0} of the states required no lookaheads and {1} state(s) contained irremovable shift/reduce conflicts.",
                numLR0states, numConflictStates);
        }
    }
}