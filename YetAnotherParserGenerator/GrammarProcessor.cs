using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YetAnotherParserGenerator.Utilities;

namespace YetAnotherParserGenerator
{
    /// <summary>
    /// The class responsible for computing the ParseTable and GotoTable of a Grammar.
    /// </summary>
    public partial class GrammarProcessor
    {
        /// <summary>
        /// Defines the various stages of lookahead computation at which a state can be resolved.
        /// </summary>
        enum LookaheadComplexity
        {
            /// <summary>
            /// This state is still not resolved.
            /// </summary>
            Unresolved,
            /// <summary>
            /// This state has been resolved with the use of LALR(1) lookahead sets.
            /// </summary>
            LALR1,
            /// <summary>
            /// This state has been resolved with the use of SLR(1) lookahead sets.
            /// </summary>
            SLR1,
            /// <summary>
            /// This state had no conflicting items in the LR(0) automaton.
            /// </summary>
            LR0
        }

        /// <summary>
        /// Represents a general graph oracle.
        /// </summary>
        /// <typeparam name="TVertex">The type representing a vertex of the graph.</typeparam>
        abstract class EdgeOracle<TVertex>
        {
            /// <summary>
            /// Gets the neighbours of the vertex <i>v</i>.
            /// </summary>
            /// <param name="v">The vertex whose neighbours are to be found.</param>
            /// <returns>The neighbours of <i>v</i>.</returns>
            abstract public IEnumerable<TVertex> GetNeighboursFor(TVertex v);
        }

        /// <summary>
        /// Represents an oracle happy to divulge the edges of the <i>reads</i> relation.
        /// </summary>
        class ReadsOracle : EdgeOracle<NonterminalTransition>
        {
            private GrammarProcessor processor;

            /// <summary>
            /// Initializes a new instance of ReadsOracle, supplying it with a link to the grammar data.
            /// </summary>
            /// <param name="processor">The GrammarProcessor instance which is processing the Grammar in question.</param>
            public ReadsOracle(GrammarProcessor processor)
            {
                this.processor = processor;
            }

            /// <summary>
            /// Gets the neigbours of <i>trans</i> in the <i>reads</i> relation.
            /// </summary>
            /// <param name="trans">The NonterminalTransition whose neighbours in <i>reads</i> are to be found.</param>
            /// <returns>The neighbours of <i>trans</i> in the <i>reads</i> relation.</returns>
            public override IEnumerable<NonterminalTransition> GetNeighboursFor(NonterminalTransition trans)
            {
                State nextState = trans.Destination;
                foreach (Transition nextTrans in nextState.Transitions)
                    if ((nextTrans is NonterminalTransition) &&
                        (processor.nonterminalNullable[nextTrans.TransitionSymbol - processor.grammar.NumTerminals]))
                        yield return nextTrans as NonterminalTransition;
            }
        }

        /// <summary>
        /// Represents an oracle willing to divulge the edges of the <i>includes</i> relation.
        /// </summary>
        class IncludesOracle : EdgeOracle<NonterminalTransition>
        {
            /// <summary>
            /// A tuple consisting of a distance in a graph and a symbol's code.
            /// </summary>
            struct Stop
            {
                /// <summary>
                /// The distance at which we are looking for the nonterminal transitions.
                /// </summary>
                public int Distance;
                /// <summary>
                /// The code of a symbol whose nonterminal transitions we are seeking.
                /// </summary>
                public int Symbol;
            }

            private GrammarProcessor processor;

            /// <summary>
            /// Initializes a new instance of ReadsOracle, supplying it with a link to the grammar data.
            /// </summary>
            /// <param name="processor">The GrammarProcessor instance which is processing the Grammar in question.</param>
            public IncludesOracle(GrammarProcessor processor)
            {
                this.processor = processor;
            }

            /// <summary>
            /// Gets the neigbours of <i>trans</i> in the <i>includes</i> relation.
            /// </summary>
            /// <param name="trans">The NonterminalTransition whose neighbours in <i>includes</i> are to be found.</param>
            /// <returns>The neighbours of <i>trans</i> in the <i>includes</i> relation.</returns>
            public override IEnumerable<NonterminalTransition> GetNeighboursFor(NonterminalTransition trans)
            {
                //budou nás zajímat všechny itemy tvaru:
                //<A> ::= *k symbolů* trans.TransitionSymbol . *nulovatelné neterminály*;
                //Budeme cestovat ze stavu trans.Source po k zpětných hranách a pokud najdeme hranu označenou
                //symbolem <A>, tak ji ohlásíme jako souseda hrany trans v relaci includes.
                //Abychom po zpětných hranách nemuseli cestovat pro každý zajímavý item zvlášť, zapíšeme
                //si dopředu, které hrany nás budou v jakých vzdálenostech zajímat a pak je vyřešíme všechny
                //v jednom průchodu.

                List<Stop> stops = new List<Stop>();
                int maxDistance = 0;

                foreach (Item item in trans.Destination.ItemSet)
                {
                    Production production = item.Production;
                    for (int i = item.Position; i < production.RHSSymbols.Count; i++)
                        if ((production.RHSSymbols[i] < processor.grammar.NumTerminals) ||
                            (!processor.nonterminalNullable[production.RHSSymbols[i] - processor.grammar.NumTerminals]))
                            continue;

                    for (int i = production.RHSSymbols.Count - 1; i >= 0; i--)
                    {
                        if (production.RHSSymbols[i] == trans.TransitionSymbol)
                        {
                            Stop newStop = new Stop();
                            newStop.Distance = i;
                            newStop.Symbol = production.LHSSymbol;
                            maxDistance = Math.Max(i, maxDistance);
                            stops.Add(newStop);
                        }
                        if ((production.RHSSymbols[i] < processor.grammar.NumTerminals) ||
                           (!processor.nonterminalNullable[production.RHSSymbols[i] - processor.grammar.NumTerminals]))
                            break;
                    }
                }

                stops.Sort((x, y) => x.Distance.CompareTo(y.Distance));

                int stopsProcessed = 0;

                List<State> lookbackStates = new List<State>() { trans.Source };

                while ((stopsProcessed < stops.Count) && (stops[stopsProcessed].Distance == 0))
                {
                    foreach (Transition nextTrans in lookbackStates[0].Transitions)
                        if (nextTrans.TransitionSymbol == stops[stopsProcessed].Symbol)
                        {
                            yield return nextTrans as NonterminalTransition;
                            break;
                        }
                    stopsProcessed++;
                }

                int statesPassed = 0;

                for (int dist = 1; dist <= maxDistance; dist++)
                {
                    int statesToPass = lookbackStates.Count - statesPassed;

                    for (int k = 0; k < statesToPass; k++)
                        lookbackStates.AddRange(lookbackStates[statesPassed + k].AccessingStates);

                    statesPassed += statesToPass;

                    while ((stopsProcessed < stops.Count) && (stops[stopsProcessed].Distance == dist))
                    {
                        for (int state = statesPassed; state < lookbackStates.Count; state++)
                            foreach (Transition nextTrans in lookbackStates[state].Transitions)
                                if (nextTrans.TransitionSymbol == stops[stopsProcessed].Symbol)
                                {
                                    yield return nextTrans as NonterminalTransition;
                                    break;
                                }
                        stopsProcessed++;
                    }
                }
            }
        }

        /// <summary>
        /// Represents an oracle which reveals the edges of the <i>follows</i> relation.
        /// </summary>
        class SLROracle : EdgeOracle<int>
        {
            private GrammarProcessor processor;

            /// <summary>
            /// Initializes a new instance of SLROracle, supplying it with a link to the grammar data.
            /// </summary>
            /// <param name="processor">The GrammarProcessor instance which is processing the Grammar in question.</param>
            public SLROracle(GrammarProcessor processor)
            {
                this.processor = processor;
            }

            /// <summary>
            /// Gets the neigbours of <i>nonterminal</i> in the <i>follows</i> relation.
            /// </summary>
            /// <param name="nonterminal">The nonterminal whose neighbours in <i>follows</i> are to be found.</param>
            /// <returns>The neighbours of <i>nonterminal</i> in the <i>follows</i> relation.</returns>
            public override IEnumerable<int> GetNeighboursFor(int nonterminal)
            {
                //jednodušše se u každého přepisovacího pravidla, kde se nonterminal vyskytuje na pravé straně
                //podíváme, zda-li je následován pouze nulovatelnými neterminály
                foreach (Production production in processor.productionsByRHSNonterminals[nonterminal - processor.grammar.NumTerminals])
                {
                    bool isLast = true;
                    for (int i = production.RHSSymbols.Count - 1; i >= 0; i--)
                        if (production.RHSSymbols[i] == nonterminal)
                        {
                            isLast = true;
                            break;
                        }
                        else if ((production.RHSSymbols[i] < processor.grammar.NumTerminals) ||
                                 (!processor.nonterminalNullable[production.RHSSymbols[i] - processor.grammar.NumTerminals]))
                        {
                            isLast = false;
                            break;
                        }


                    if (isLast)
                        yield return production.LHSSymbol;
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of GrammarProcessor.
        /// </summary>
        public GrammarProcessor()
        {
        }

        //gramatika, jejíž tabulky dopočítáváme; GrammarProcessor si data týkající jednoho konkrétního volání
        //ukládá do členských proměnných, tudíž jeho metodu ComputeTables nelze volat vícekrát současně.
        //Pro tento případ však třída GrammarProcessor není statická a tudíž je vždy možné si vytvořit další
        //instanci, pokud by bylo zapotřebí kompilovat několik gramatik současně
        private Grammar grammar;
        //stavy LR automatu, vyrobené rekurzivní funkcí exploreTransitions
        private List<State> parserStates;

        //pro každý neterminál si uchováváme seznam přepisovacích pravidel, ve kterých se objevuje na pravé straně;
        //důležité pro výpočet relace SLR-follows, hodí se i pro výpočet nulovatelných neterminalů,
        private List<Production>[] productionsByRHSNonterminals;
        //pro každý neterminál si také budeme uchovávat seznam hran, které jsou daným neterminálem označené;
        //používáme při výpočtu počátečních hodnot pro algoritmus digraph použitý na hledání SLR(1) lookaheadů
        //(initSLR)
        private List<NonterminalTransition>[] transitionsByNonterminals;

        //počet neterminálních hran v grafu automatu
        private int numNonterminalTransitions;

        //pro každý stav si budeme pamatovat, kdy a zda-li jsem jej vyřešili
        private LookaheadComplexity[] stateResolvedAt;

        //pro každý ne-LR(0) stav si budeme uchovávat seznam finálních itemů
        private List<List<Item>> conflictingItems;
        //pro každý ne-LR(0) stav si budeme pamatovat lookahead pro každý finální item
        private List<List<BitVectorSet>> lookaheadSets;
        //pro každý stav si budeme pamatovat, které seznamy v polích conflictingItems a lookaheadSets mu patří,
        //pokud je stav LR(0) (nepotřebuje lookahead), hodnota se bude rovnat -1
        private int[] stateLookaheadIndex;

        //počet stále nevyřešených stavů
        private int numInconsistentStates;

        //pro každý neterminál uchovává to, zda-li je nulovatelný
        private bool[] nonterminalNullable;

        //pomocný zásobník pro algoritmus digraph; uchováváme tady jeden, protože digraph po sobě vždy uklidí
        //a bylo by tedy zbytečné, aby si vytvářel pokaždé nový
        private Stack<int> S = new Stack<int>();

        //funkce, které převádějí nějaký objekt na ordinálu v intervalu 0..počet objektů
        //používané v zobecnění algoritmu Digraph pro výpočet jak SLR(1) lookaheadů nad neterminály,
        //tak na výpočet LALR(1) lookaheadů nad neterminálními hranami
        private Func<NonterminalTransition, int> getTransNumber = (trans => trans.NonterminalTransitionNumber);
        private Func<int, int> getNontermIndex;

        //množinové funkce Read, Follow a SLR-Follow takové, jak jsou definovány v DeRemerovi a Pennellovi
        private BitVectorSet[] read, follow, slr_follow;

        //pomocné pole N algoritmu Digraph; máme jedno zvlášť pro každý graf, na kterém algoritmus Digraph spouštíme
        //(graf neterm. hran indukovaný relacemi reads a includes a graf neterminálů indukovaný relací SLR-follows)
        private int[] N_reads, N_includes, N_slr;

        //orákula pro všechny tři relace
        private ReadsOracle reads;
        private IncludesOracle includes;
        private SLROracle slr_follows;

        //funkce, která neterminální hraně přiřadi její Direct Read množinu, která slouží jako základ pro výpočet read množin
        private Func<NonterminalTransition, BitVectorSet> initDR;

        //funkce, která neterminální hraně přiřadí její Read množinu, která slouží jako základ pro výpočet follow množin
        private Func<NonterminalTransition, BitVectorSet> initRead;

        //funkce, která neterminálním symbolům přiřazuje množiny, které po propagaci algoritmem Digraph
        //vykvetou do množin SLR-Follow
        private Func<int, BitVectorSet> initSLR;


        /// <summary>
        /// Gets or sets whether the GrammarProcessor always computes the LALR(1) lookahead sets even though
        /// SLR(1) lookahead sets would be adequate to resolve conflicts in some states.
        /// </summary>
        public bool ForceLALR1Lookaheads
        {
            get { return forceLalr1; }
            set { forceLalr1 = value; }
        }
        private bool forceLalr1 = false;

        /// <summary>
        /// Computes the ParseTable and GotoTable of a Grammar's ParserData.
        /// </summary>
        /// <param name="grammar">The Grammar whose tables are to be computed. GrammarDefinition ought to be
        /// set and filled with appropriate data and ParserData should be initialized.</param>
        public void ComputeTables(Grammar grammar)
        {
            ComputeTables(grammar, null);
        }

        /// <summary>
        /// Computes the ParseTable and GotoTable of a Grammar's ParserData, logging the automata's graph
        /// to a logfile should the <i>grammar</i> prove to be non-LALR(1).
        /// </summary>
        /// <param name="grammar">The Grammar whose tables are to be computed. GrammarDefinition ought to be
        /// set and filled with appropriate data and ParserData should be initialized.</param>
        /// <param name="logfileName">The name of the file to which the nondeterministic automaton
        /// is to be written in case the <i>grammar</i> is not LALR(1).</param>
        public void ComputeTables(Grammar grammar, string logfileName)
        {
            ComputeTables(grammar, logfileName, false);
        }

        /// <summary>
        /// Computes the ParseTable and GotoTable of a Grammar's ParserData, logging the automata's graph
        /// to a logfile should the <i>grammar</i> prove to be non-LALR(1) or should the caller explicitly
        /// state he wants a log.
        /// </summary>
        /// <param name="grammar">The Grammar whose tables are to be computed. GrammarDefinition ought to be
        /// set and filled with appropriate data and ParserData should be initialized.</param>
        /// <param name="logfileName">The name of the file to which the automaton is to be logged.</param>
        /// <param name="explicitLogging">A Boolean value determining whether the automaton should be
        /// written to the logfile even though there are no inconsistencies.</param>
        public void ComputeTables(Grammar grammar, string logfileName, bool explicitLogging)
        {
            ComputeTables(grammar, logfileName, explicitLogging, null);
        }

        /// <summary>
        /// Computes the ParseTable and GotoTable of a Grammar's ParserData, logging the automata's graph
        /// to a logfile should the <i>grammar</i> prove to be non-LALR(1) or should the caller explicitly
        /// state he wants a log. Any reports generated by the processor will be sent to the <i>reportOutput</i>
        /// TextWriter instance.
        /// </summary>
        /// <param name="grammar">The Grammar whose tables are to be computed. GrammarDefinition ought to be
        /// set and filled with appropriate data and ParserData should be initialized.</param>
        /// <param name="logfileName">The name of the file to which the automaton is to be logged; <b>null</b>
        /// if logging should be disabled.</param>
        /// <param name="explicitLogging">A Boolean value determining whether the automaton should be
        /// written to the logfile even though there are no inconsistencies.</param>
        /// <param name="reportOutput">The TextWriter to which the report should be written; <b>null</b>
        /// if reporting should be disabled.</param>
        public void ComputeTables(Grammar grammar, string logfileName, bool explicitLogging, TextWriter reportOutput)
        {
            // INICIALIZACE

            this.grammar = grammar;

            //inicializace a výpočet productionsByRHSNonterminals
            productionsByRHSNonterminals = new List<Production>[grammar.GrammarDefinition.NumNonterminals];
            for (int nonterminal = 0; nonterminal < productionsByRHSNonterminals.Length; nonterminal++)
                productionsByRHSNonterminals[nonterminal] = new List<Production>();

            foreach (Production production in grammar.Productions)
                foreach (int rhsSymbol in production.RHSSymbols)
                    if (rhsSymbol >= grammar.NumTerminals)
                        productionsByRHSNonterminals[rhsSymbol - grammar.NumTerminals].Add(production);

            //inicializace transitionsByNonterminals, hodnoty jsou do seznamů posléze nasázeny ve funkci
            //exploreTransitions, která zároveň vyrábí LR(0) automat
            transitionsByNonterminals = new List<NonterminalTransition>[grammar.NumNonterminals];
            for (int nonterminal = 0; nonterminal < grammar.NumNonterminals; nonterminal++)
                transitionsByNonterminals[nonterminal] = new List<NonterminalTransition>();

            numNonterminalTransitions = 0;

            conflictingItems = new List<List<Item>>();
            lookaheadSets = new List<List<BitVectorSet>>();


            parserStates = new List<State>();

            // TVORBA LR(0) AUTOMATU

            //vytvoříme počáteční ItemSet a nastartujeme rekurzivní
            //exploreTransitions

            Item startItem = new Item(grammar.Productions[0], 0);
            ItemSet startIS = new ItemSet();
            startIS.Add(startItem);
            startIS.CloseItemSet(grammar);

            State initialState = new State(0, startIS);
            parserStates.Add(initialState);

            //spočítá nám parserStates, hrany mezi nimi, nonterminalTransitions (počet neterminálních hran)
            //a transitionsByNonterminals
            exploreTransitions(initialState);


            //tenhle kousek inicializace si musel počkat na dopočítání stavů automatu
            stateLookaheadIndex = new int[parserStates.Count];
            for (int i = 0; i < parserStates.Count; i++)
                stateLookaheadIndex[i] = -1;

            //původní hodnota Look
            stateResolvedAt = new LookaheadComplexity[parserStates.Count];


            // ŘEŠENÍ NEDETERMINISTICKÝCH STAVŮ (KONFLIKTŮ)

            numInconsistentStates = 0;

            foreach (State state in parserStates)
            {
                List<Item> finalItems = new List<Item>();
                stateResolvedAt[state.StateNumber] = LookaheadComplexity.LR0;

                foreach (Item item in state.ItemSet)
                    if (item.IsFinal)
                        finalItems.Add(item);

                if (finalItems.Count >= 2)
                {
                    stateLookaheadIndex[state.StateNumber] = numInconsistentStates;
                    stateResolvedAt[state.StateNumber] = LookaheadComplexity.Unresolved;
                    numInconsistentStates++;
                    conflictingItems.Add(finalItems);
                }
                else if (finalItems.Count >= 1)
                {
                    bool canRead = false;
                    foreach (Transition trans in state.Transitions)
                        if (trans is TerminalTransition)
                        {
                            canRead = true;
                            break;
                        }
                    if (canRead)
                    {
                        stateLookaheadIndex[state.StateNumber] = numInconsistentStates;
                        stateResolvedAt[state.StateNumber] = LookaheadComplexity.Unresolved;
                        numInconsistentStates++;
                        conflictingItems.Add(finalItems);
                    }
                }
            }

            if (numInconsistentStates > 0)
            {
                //Vstupní gramatika není LR(0), bude tedy třeba spočítat lookahead množiny pro nekonzistentní
                //stavy. Použijeme postup DeRemera a Pennella, kdy se pokusíme každý nekonzistení stav nejdříve
                //vyřešit pomocí SLR(1) lookahead množin a až poté případně přikročíme k výpočtu LALR(1) lookaheadů.

                //Krok 1. Určit, které neterminály jsou nulovatelné.

                computeNullableNonterminals();

                //Krok 2. Spočítat SLR(1) lookaheady.
                //Připravíme se na počítání Read a SLR-Follow množin a pokusíme se vyřešit konflikty
                //pouze pomocí SLR(1) lookaheadů.


                //Direct Read množina pro každou neterminální hranu
                initDR =
                    (trans =>
                    {
                        BitVectorSet set = new BitVectorSet(grammar.NumTerminals);
                        foreach (Transition nextTrans in trans.Destination.Transitions)
                            if (nextTrans is TerminalTransition)
                                set.Add(nextTrans.TransitionSymbol);
                        return set;
                    });

                read = new BitVectorSet[numNonterminalTransitions];
                N_reads = new int[numNonterminalTransitions];
                reads = new ReadsOracle(this);

                if (!forceLalr1)
                {
                    getNontermIndex = (nonterm => nonterm - grammar.NumTerminals);

                    //původní hodnota pro nějaký neterminál bude sjednocení Read množin všech hran označených
                    //tímto neterminálem; vyplývá téměř přímo z definice výpočtu Follow množin SLR(1) parserů
                    initSLR = (nonterm =>
                    {
                        BitVectorSet set = new BitVectorSet(grammar.NumTerminals);
                        foreach (NonterminalTransition trans in transitionsByNonterminals[nonterm - grammar.NumTerminals])
                        {
                            if (N_reads[getTransNumber(trans)] == 0)
                                digraphTraverse<NonterminalTransition>(trans, N_reads, read, reads, initDR, getTransNumber);
                            set.UnionWith(read[getTransNumber(trans)]);
                        }
                        return set;
                    });

                    slr_follow = new BitVectorSet[grammar.NumNonterminals];
                    N_slr = new int[grammar.NumNonterminals];
                    slr_follows = new SLROracle(this);

                    foreach (State state in parserStates)
                        if (stateResolvedAt[state.StateNumber] == LookaheadComplexity.Unresolved)
                        {
                            List<BitVectorSet> stateLookaheads = new List<BitVectorSet>();

                            foreach (Item conflictItem in conflictingItems[stateLookaheadIndex[state.StateNumber]])
                            {
                                if (N_slr[getNontermIndex(conflictItem.Production.LHSSymbol)] == 0)
                                    digraphTraverse<int>(conflictItem.Production.LHSSymbol, N_slr, slr_follow, slr_follows, initSLR, getNontermIndex);

                                stateLookaheads.Add(slr_follow[getNontermIndex(conflictItem.Production.LHSSymbol)]);
                            }

                            lookaheadSets.Add(stateLookaheads);
                        }
                }

                //Krok 3. Spočítat LALR(1) lookaheady.
                //Pokud SLR(1) lookaheady nevyřešily všechny konflikty, spočteme pro nedořešené stavy
                //LALR(1) lookaheady.

                if (forceLalr1 || checkForConflicts(LookaheadComplexity.SLR1))
                {
                    initRead = (trans =>
                    {
                        if (N_reads[getTransNumber(trans)] == 0)
                            digraphTraverse<NonterminalTransition>(trans, N_reads, read, reads, initDR, getTransNumber);
                        return new BitVectorSet(read[getTransNumber(trans)]);
                    });

                    follow = new BitVectorSet[numNonterminalTransitions];
                    N_includes = new int[numNonterminalTransitions];
                    includes = new IncludesOracle(this);

                    foreach (State state in parserStates)
                        if (stateResolvedAt[state.StateNumber] == LookaheadComplexity.Unresolved)
                        {
                            List<BitVectorSet> stateLookaheads = new List<BitVectorSet>();

                            foreach (Item conflictItem in conflictingItems[stateLookaheadIndex[state.StateNumber]])
                            {
                                BitVectorSet lookaheadSet = new BitVectorSet(grammar.NumTerminals);

                                foreach (NonterminalTransition trans in lookback(state, conflictItem))
                                {
                                    if (N_includes[getTransNumber(trans)] == 0)
                                        digraphTraverse<NonterminalTransition>(trans, N_includes, follow, includes, initRead, getTransNumber);

                                    lookaheadSet.UnionWith(follow[getTransNumber(trans)]);
                                }

                                stateLookaheads.Add(lookaheadSet);
                            }

                            //v případě, že je tohle naše první počítání lookahead množin, tak musíme
                            //založit pro stav novou položku v seznamu lookaheadSets; v opačném případě
                            //přepíšeme tu, kterou jsme vytvořili při počítání minulém
                            if (forceLalr1)
                                lookaheadSets.Add(stateLookaheads);
                            else
                                lookaheadSets[stateLookaheadIndex[state.StateNumber]] = stateLookaheads;
                        }

                    //Krok 4. Ověřit parser
                    //Pokud parser stále obsahuje konflikty, vypíšeme uživateli do logu podobu stavového
                    //automatu a vyznačíme v ní konflikty. Pokud parser konflikty neobsahuje, zapíšeme
                    //poznatky do tabulek a máme hotovo.

                    bool reduceReduceConflicts;
                    bool conflicts = checkForConflicts(LookaheadComplexity.LALR1, out reduceReduceConflicts);
                    if (reduceReduceConflicts)
                    {
                        if (logfileName != null)
                        {
                            printAutomatonStates(logfileName);
                            throw new GrammarException(string.Format("Reduce/reduce conflicts detected in the resulting parser.\r\nThe grammar isn't LALR(1).\r\nCheck the log file {0} for details.", logfileName));
                        }
                        else
                            throw new GrammarException("Reduce/reduce conflicts detected in the resulting parser.\r\nThe grammar isn't LALR(1).");
                    }
                    else if (conflicts)
                    {
                        if (reportOutput != null)
                            printShiftReduceConflicts(reportOutput);
                    }
                }
            }

            ParserAction[,] parseTable = new ParserAction[parserStates.Count, grammar.NumTerminals];
            int[,] gotoTable = new int[parserStates.Count, grammar.NumNonterminals];
            for (int i = 0; i < parserStates.Count; i++)
                for (int j = 0; j < grammar.NumNonterminals; j++)
                    gotoTable[i, j] = -1;

            for (int stateNumber = 0; stateNumber < parserStates.Count; stateNumber++)
            {
                if (stateLookaheadIndex[stateNumber] >= 0)
                {
                    for (int i = 0; i < conflictingItems[stateLookaheadIndex[stateNumber]].Count; i++)
                    {
                        ParserAction action = new ParserAction();
                        action.ActionType = ParserActionType.Reduce;
                        action.Argument = conflictingItems[stateLookaheadIndex[stateNumber]][i].Production.ProductionCode;
                        foreach (int symbol in lookaheadSets[stateLookaheadIndex[stateNumber]][i])
                            parseTable[stateNumber, symbol] = action;
                    }
                }
                else
                {
                    foreach (Item item in parserStates[stateNumber].ItemSet)
                        if (item.IsFinal)
                        {
                            ParserAction action = new ParserAction();
                            action.ActionType = ParserActionType.Reduce;
                            action.Argument = item.Production.ProductionCode;
                            for (int symbol = 0; symbol < grammar.NumTerminals; symbol++)
                                parseTable[stateNumber, symbol] = action;
                        }
                }

                foreach (Transition trans in parserStates[stateNumber].Transitions)
                {
                    if (trans is TerminalTransition)
                    {
                        parseTable[stateNumber, trans.TransitionSymbol].ActionType = ParserActionType.Shift;
                        parseTable[stateNumber, trans.TransitionSymbol].Argument = trans.Destination.StateNumber;
                    }
                    else
                        gotoTable[stateNumber, trans.TransitionSymbol - grammar.NumTerminals] = trans.Destination.StateNumber;
                }
            }

            grammar.ParserData.ParseTable = parseTable;
            grammar.ParserData.GotoTable = gotoTable;

            if (explicitLogging)
                printAutomatonStates(logfileName);

            if (reportOutput != null)
                printSuccessReport(reportOutput);
        }

        //pro danou dvojici stav-item spočítá sousedy v relaci lookback
        private IEnumerable<NonterminalTransition> lookback(State state, Item finalItem)
        {
            //chceme najít stavy, ze kterých vede cesta označená symboly na pravé straně přepisovacího
            //pravidla ve finalItem; všimneme si, že všechny hrany vedoucí do nějakého stavu
            //jsou stejně ohodnoceny a tudíž nám stačí najít všechny stavy, do kterých se lze dostat
            //sledy délky k v grafu s opačně orientovanými hranami, kde k je počet symbolů
            //na pravé straně přepisovacího pravidla z finalItem

            //stavy, do kterých se dostaneme po nula zpětných hranách hraně
            List<State> lookbackStates = new List<State>() { state };

            //a tady se posuneme o k zpětných hran "dál"
            int statesPassed = 0;
            for (int i = 0; i < finalItem.Production.RHSSymbols.Count; i++)
            {
                int statesToPass = lookbackStates.Count - statesPassed;
                for (int k = 0; k < statesToPass; k++)
                    lookbackStates.AddRange(lookbackStates[statesPassed + k].AccessingStates);
                statesPassed += statesToPass;
            }

            //z těchto stavů nás zajímají všechny hrany označené symbolem na levé straně přepisovacího
            //pravidla z finalItem
            for (int i = statesPassed; i < lookbackStates.Count; i++)
                foreach (Transition trans in lookbackStates[i].Transitions)
                    if (trans.TransitionSymbol == finalItem.Production.LHSSymbol)
                    {
                        yield return (trans as NonterminalTransition);
                        break;
                    }
        }

        //rekurzivně projde ještě neexistující graf stavů automatu; nové vrcholy objevuje zkoumáním
        //ItemSetu
        private void exploreTransitions(State thisState)
        {
            //symboly, jež příslušejí hranám vedoucím z tohoto stavu
            IEnumerable<int> appropriateSymbols = (from item in thisState.ItemSet
                                                   where !item.IsFinal
                                                   select item.Production.RHSSymbols[item.Position]).Distinct();

            foreach (int symbol in appropriateSymbols)
            {
                ItemSet successorIS = thisState.ItemSet.NucleusAfterTransition(symbol);
                successorIS.CloseItemSet(grammar);

                //podíváme se, jestli jsme na stav s tímto ItemSetem už nenarazili
                int successorIndex;
                for (successorIndex = 0; successorIndex < parserStates.Count; successorIndex++)
                {
                    if (successorIS.SetEquals(parserStates[successorIndex].ItemSet))
                        break;
                }

                State successorState;
                if (successorIndex == parserStates.Count)
                {
                    successorState = new State(successorIndex, successorIS);
                    parserStates.Add(successorState);

                    exploreTransitions(successorState);
                }
                else
                    successorState = parserStates[successorIndex];


                Transition newTransition;
                if (symbol < grammar.NumTerminals)
                    newTransition = new TerminalTransition(thisState, successorState, symbol);
                else
                    newTransition = new NonterminalTransition(thisState, successorState, symbol, numNonterminalTransitions++);

                thisState.Transitions.Add(newTransition);
                successorState.AccessingStates.Add(thisState);

                if (newTransition is NonterminalTransition)
                    transitionsByNonterminals[symbol - grammar.NumTerminals].Add(newTransition as NonterminalTransition);
            }
        }

        //zjistí, které neterminály jsou nulovatelné (dají se přepsat pravidly na prázdný řetězec)
        private void computeNullableNonterminals()
        {
            //pro každé přepisovací pravidlo si budeme pamatovat, kolik symbolů na jeho pravé straně
            //není nulovatelných; jak budeme postupně nulovatelné neterminály nacházet, budeme s pomocí
            //pole productionsByRHSNonterminals tento počet patřičným pravidlům snižovat. Jsme tak schopni
            //najít nulovatelné neterminály v čase lineárním k velikosti gramatiky (počet všech výskytů symbolů
            //ve všech pravidlech)
            int[] nonnullableSymbolsRemaining = new int[grammar.NumProductions];

            foreach (Production production in grammar.Productions)
                nonnullableSymbolsRemaining[production.ProductionCode] = production.RHSSymbols.Count;

            nonterminalNullable = new bool[grammar.NumNonterminals];

            //najdeme "přímo" nulovatelné neterminály...
            Queue<int> newNullables = new Queue<int>();
            foreach (Production production in grammar.Productions)
            {
                if (nonnullableSymbolsRemaining[production.ProductionCode] == 0)
                    if (!nonterminalNullable[production.LHSSymbol - grammar.NumTerminals])
                    {
                        nonterminalNullable[production.LHSSymbol - grammar.NumTerminals] = true;
                        newNullables.Enqueue(production.LHSSymbol);
                    }
            }

            //...a necháme je šířit nulovatelnost po ostatních
            while (newNullables.Count > 0)
            {
                int nullable = newNullables.Dequeue();

                foreach (Production production in productionsByRHSNonterminals[nullable - grammar.NumTerminals])
                {
                    nonnullableSymbolsRemaining[production.ProductionCode]--;
                    if (nonnullableSymbolsRemaining[production.ProductionCode] == 0)
                        if (!nonterminalNullable[production.LHSSymbol - grammar.NumTerminals])
                        {
                            nonterminalNullable[production.LHSSymbol - grammar.NumTerminals] = true;
                            newNullables.Enqueue(production.LHSSymbol);
                        }
                }
            }
        }

        //projde všechny dosud nevyřešené stavy a podívá se, jestli byla minulá fáze výpočtu dostatečná
        //na to, aby rozřešila konflikty, které se v nich objevují; parametr lastStage popisuje, jak vypadala
        //poslední fáze výpočtu a můžeme tak pro diagnostické důvody sledovat, které stavy jsou vyřešeny
        //v kterém stádiu výpočtu
        private bool checkForConflicts(LookaheadComplexity lastStage)
        {
            bool conflictsInAutomaton = false;

            foreach (State state in parserStates)
                if (stateResolvedAt[state.StateNumber] == LookaheadComplexity.Unresolved)
                {
                    BitVectorSet accumulator = new BitVectorSet(grammar.NumTerminals);

                    //inicializujeme akumulační proměnnou na množinu terminálů, které v daném stavu můžeme přečíst
                    foreach (Transition trans in state.Transitions)
                        if (trans is TerminalTransition)
                            accumulator.Add(trans.TransitionSymbol);

                    bool conflictsInThisState = false;

                    //akumulační proměnná prochází kolem všech lookahead množin a testuje se s nimi na disjunktnost,
                    //sama potom přebírá jejich prvky (nemusíme tak testovat každou dvojici množin)
                    foreach (BitVectorSet lookaheadSet in lookaheadSets[stateLookaheadIndex[state.StateNumber]])
                        if (!accumulator.IsDisjointWith(lookaheadSet))
                        {
                            conflictsInThisState = true;
                            break;
                        }
                        else
                            accumulator.UnionWith(lookaheadSet);

                    if (conflictsInThisState)
                        conflictsInAutomaton = true;
                    else
                    {
                        numInconsistentStates--;
                        stateResolvedAt[state.StateNumber] = lastStage;
                    }
                }

            return conflictsInAutomaton;
        }

        //tenhle overload se volá po posledním výpočtu lookaheadů, kde jsme ochotni tolerovat i shift/reduce
        //konflikty, kde budeme preferovat shift; proto tento overload zjišťuje, zda se v daném automatu
        //nacházejí i reduce/reduce konflikty, se kterými už bychom si neuměli rozumně poradit
        private bool checkForConflicts(LookaheadComplexity lastStage, out bool reduceReduceConflictsInAutomaton)
        {
            bool conflictsInAutomaton = false;
            reduceReduceConflictsInAutomaton = false;

            foreach (State state in parserStates)
                if (stateResolvedAt[state.StateNumber] == LookaheadComplexity.Unresolved)
                {
                    BitVectorSet accumulator = new BitVectorSet(grammar.NumTerminals);
                    BitVectorSet reduceAccumulator = new BitVectorSet(grammar.NumTerminals);

                    foreach (Transition trans in state.Transitions)
                        if (trans is TerminalTransition)
                            accumulator.Add(trans.TransitionSymbol);

                    bool conflictsInThisState = false;

                    foreach (BitVectorSet lookaheadSet in lookaheadSets[stateLookaheadIndex[state.StateNumber]])
                        if (!reduceAccumulator.IsDisjointWith(lookaheadSet))
                        {
                            conflictsInThisState = true;
                            reduceReduceConflictsInAutomaton = true;
                            break;
                        }
                        else
                        {
                            if (!accumulator.IsDisjointWith(lookaheadSet))
                                conflictsInThisState = true;

                            reduceAccumulator.UnionWith(lookaheadSet);
                        }

                    if (conflictsInThisState)
                        conflictsInAutomaton = true;
                    else
                    {
                        numInconsistentStates--;
                        stateResolvedAt[state.StateNumber] = lastStage;
                    }
                }

            return conflictsInAutomaton;
        }

        //algoritmus Digraph na hledání komponent silné souvislosti, jehož správnost jsem dokazoval v zápočtové
        //práci z ADS; zde upraven, aby počítal reflexivní tranzitivní uzávěr relace pro výpočet množinové funkce
        //F definované jako F(x) = I(x) ∪ ⋃{F(y) | xRy}
        private void digraphTraverse<TVertex>(TVertex x, int[] N, BitVectorSet[] F, EdgeOracle<TVertex> R,
                                    Func<TVertex, BitVectorSet> I, Func<TVertex, int> getVertexIndex)
        {
            int xOrd = getVertexIndex(x);

            S.Push(xOrd);
            int d = S.Count;
            N[xOrd] = d;
            F[xOrd] = I(x);

            foreach (TVertex y in R.GetNeighboursFor(x))
            {
                int yOrd = getVertexIndex(y);

                if (N[yOrd] == 0)
                    digraphTraverse<TVertex>(y, N, F, R, I, getVertexIndex);

                N[xOrd] = Math.Min(N[xOrd], N[yOrd]);
                F[xOrd].UnionWith(F[yOrd]);
            }

            if (N[xOrd] == d)
                do
                {
                    N[S.Peek()] = int.MaxValue;
                    //pokud se vršek S rovná x, potom si odpustíme kopírování F[x] do F[vršek S];
                    //tato malá optimalizace je důležitá, jelikož většina SSK bude triviálních (1 vrchol)
                    if (S.Peek() != xOrd)
                        F[S.Peek()] = new BitVectorSet(F[xOrd]);
                }
                while (S.Pop() != xOrd);
        }
    }
}
