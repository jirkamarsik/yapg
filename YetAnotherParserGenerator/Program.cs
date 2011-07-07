using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Linq;
using System.Text;
using YetAnotherParserGenerator.Utilities;

namespace YetAnotherParserGenerator
{
    /// <summary>
    /// The class representing the program faced by the users when launched as an executable.
    /// </summary>
    public class Yapg
    {
        private static string usage = @"yapg make inputFile [-o outputFile] [-l logFile] [-f] [-c compilerOptions]
yapg run parserFile

'yapg make' reads the inputFile and creates a parser for the grammar
specified in it. It stores the result in binary form to the outputFile
or to inputFile.par if outputFile is not specified. If the grammar is found
to be non-LALR(1), the automaton's states will be logged to logFile or
logFile.html if logFile is not specified. If logFile is specified, the log
will be produced even if the grammar is LALR(1). The '-f' option instructs
the grammar processor to compute LALR(1) lookahead sets for all final items
in all conflicting states, even though their conflicts could be resolved
via SLR(1) lookahead sets. Using the '-f' option generally results in longer
processing time but creates a parser with better error reporting. More specific
options for the C# compiler can be given via the '-c' option.

'yapg run' reads the binary runtime data of a parser from parserFile and
then expects input from the standard input stream. After it has received
all the input, it will parse it and write the results to the standard
output.";

        /// <summary>
        /// The Main method when launched as an executable. Can be used to trigger the program's behaviour.
        /// </summary>
        /// <param name="args">Command-line arguments (check documentation or usage for more details).</param>
        public static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                wrongUse();
                return;
            }

            try
            {
                switch (args[0].ToLower())
                {
                    case "make":
                        make(args);
                        break;
                    case "run":
                        run(args);
                        break;
                    case "help":
                    case "usage":
                        Console.WriteLine(usage);
                        break;
                    default:
                        wrongUse();
                        return;
                }
            }
            catch (UserInputException exc)
            {
                Console.Error.WriteLine(exc.Message);
            }
            catch (GrammarException exc)
            {
                Console.Error.WriteLine(exc.Message);
            }
            catch (ParsingException exc)
            {
                Console.Error.WriteLine("{0},{1}: {2}", exc.LineNumber, exc.ColumnNumner, exc.Message);
            }
            catch (InvalidSpecificationException exc)
            {
                Console.Error.WriteLine(exc.Message);
            }
            catch (FileNotFoundException exc)
            {
                Console.Error.WriteLine(exc.Message);
            }
            catch (IOException exc)
            {
                Console.Error.WriteLine(exc.Message);
            }
        }

        private static void make(string[] args)
        {
            int argumentIndex = 1;
            string inputFile = null, outputFile = null, logFile = null;
            bool explicitLogging = false, forceLalr1 = false;
            string compilerOptions = "";

            while (argumentIndex < args.Length)
                switch (args[argumentIndex].ToLower())
                {
                    case "-o":
                        if ((argumentIndex + 1 >= args.Length) || (args[argumentIndex + 1][0] == '-'))
                            throw new UserInputException("The '-o' option must be followed by the name of the output file.");
                        if (outputFile != null)
                            throw new UserInputException("'yapg make' accepts only one output file.");
                        outputFile = args[argumentIndex + 1];
                        argumentIndex += 2;
                        break;
                    case "-l":
                        if ((argumentIndex + 1 >= args.Length) || (args[argumentIndex + 1][0] == '-'))
                            throw new UserInputException("The '-l' option must be followed by the name of the log file.");
                        if (logFile != null)
                            throw new UserInputException("'yapg make' logs debugging info to only one file, but more were specified.");
                        logFile = args[argumentIndex + 1];
                        argumentIndex += 2;
                        explicitLogging = true;
                        break;
                    case "-f":
                        argumentIndex++;
                        forceLalr1 = true;
                        break;
                    case "-c":
						if ((argumentIndex + 1 >= args.Length))
							throw new UserInputException("The '-c' option must be followed by a string of compiler options.");
						compilerOptions += args[argumentIndex + 1];
						argumentIndex += 2;
						break;
                    default:
                        if (inputFile != null)
                            throw new UserInputException("'yapg make' accepts only one input file.");
                        inputFile = args[argumentIndex];
                        argumentIndex++;
                        break;
                }

            if (inputFile == null)
                throw new UserInputException("Missing an input file to process.");
            if (outputFile == null)
                outputFile = Path.ChangeExtension(inputFile, ".par");
            if (logFile == null)
                logFile = Path.ChangeExtension(outputFile, ".html");

            MakeGrammar(inputFile, outputFile, logFile, explicitLogging, forceLalr1, compilerOptions);
        }

        /// <summary>
        /// Does the same as calling 'yapg make'.
        /// </summary>
        /// <param name="inputFile">The name of the file containing the grammar definition.</param>
        /// <param name="outputFile">The name of the file to which the parser's runtime data will be written.</param>
        /// <param name="logFile">The name of the file to which the automaton's states should be logged.</param>
        /// <param name="explicitLogging">When set to <b>true</b>, logs the automaton's states even if the
        /// parses is alright.</param>
        /// <param name="forceLalr1"></param>
        public static void MakeGrammar(string inputFile, string outputFile, string logFile,
		                               bool explicitLogging, bool forceLalr1, string compilerOptions)
        {
            IList<string> warningMessages;
            Grammar grammar = GrammarParser.ParseGrammar(Path.GetFullPath(inputFile), out warningMessages);

            foreach (string warning in warningMessages)
                Console.Error.WriteLine(warning);

            if (warningMessages.Count > 0)
                Console.Error.WriteLine();

            GrammarProcessor processor = new GrammarProcessor();
            processor.ForceLALR1Lookaheads = forceLalr1;
            processor.ComputeTables(grammar, logFile, explicitLogging, Console.Out);
            
			GrammarCompiler compiler = new GrammarCompiler();
			compiler.CompileGrammarCode(grammar, compilerOptions);

            grammar.WriteRuntimeDataToFile(outputFile);

            Console.Out.Flush();
        }

        private static void run(string[] args)
        {
			if (args.Length > 2)
                throw new UserInputException("You cannot specify more than one parser file.");
            if (args.Length < 2)
                throw new UserInputException("Missing a grammar file specification.");

			string parserFile = args[1];
            RunGrammar(parserFile);
        }

        /// <summary>
        /// Does the same as calling 'yapg run'.
        /// </summary>
        /// <param name="parserFile">The name of the file from which the runtime parser data will be read.</param>
        public static void RunGrammar(string parserFile)
        {
            LexerData lexerData;
            ParserData parserData;
            try
            {
                Grammar.ReadRuntimeDataFromFile(parserFile, out lexerData, out parserData);
            }
            catch (System.Runtime.Serialization.SerializationException exc)
            {
                throw new UserInputException(string.Format("The file {0} doesn't contain proper parser runtime data.",
                                                           parserFile), exc);
            }

            Lexer lexer = new Lexer(lexerData);
            Parser parser = new Parser(parserData);

            string input = Console.In.ReadToEnd();
            lexer.SourceString = input;

            object result = parser.Parse(lexer, null);
            
            Console.WriteLine(result.ToString());
        }

        private static void wrongUse()
        {
            Console.Error.WriteLine("Unknown action.\r\nTry 'yapg help' or 'yapg usage' for information on program usage.");
        }
    }
}
