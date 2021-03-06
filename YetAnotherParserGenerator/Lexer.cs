﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace YetAnotherParserGenerator
{
    /// <summary>
    /// Represents an object which can act as a lexer for the Parser class.
    /// </summary>
    public interface ILexer
    {
        /// <summary>
        /// Fetches the next token found by the lexer.
        /// </summary>
        /// <returns></returns>
        Token GetNextToken();

        /// <summary>
        /// Gets whether the lexer's underlying token source has any more tokens to divulge.
        /// (including the final $end symbol)
        /// </summary>
        bool HasTokens
        {
            get;
        }
    }

    /// <summary>
    /// A Lexer that uses the .NET RegEx engine to parse a string into tokens.
    /// </summary>
    public class Lexer : ILexer
    {
        IList<int> groupSymbolCodes;
        Regex regex;
        int position = 0, lineNumber = 1, columnNumber = 1;
        string input = "";
        bool hasTokens = false;

        /// <summary>
        /// Constructs a Lexer using the necessary data stored in the LexerData instance.
        /// </summary>
        /// <param name="lexerData">The LexerData containing information necessary to construct the Lexer.</param>
        public Lexer(LexerData lexerData)
        {
            this.groupSymbolCodes = lexerData.GroupSymbolCodes;
            this.regex = lexerData.Regex;
        }

        /// <summary>
        /// Constructs a Lexer using the individual pieces of data necessary.
        /// </summary>
        /// <param name="regex">The Regex which captures tokens from the source string.</param>
        /// <param name="groupSymbolCodes">An array of symbol codes defining what token should be returned
        /// when a given capture group matches a string. <i>groupSymbolCodes</i>[<i>i</i>] is the symbol code of the terminal
        /// whose token is to be returned when a capture group named '__'<i>i</i> matches a string.</param>
        public Lexer(Regex regex, IList<int> groupSymbolCodes)
        {
            this.regex = regex;
            this.groupSymbolCodes = groupSymbolCodes;
        }

        /// <summary>
        /// Fetches the next token from the source string.
        /// </summary>
        /// <returns>The next token discovered in the input string.</returns>
        public Token GetNextToken()
        {
            if (position < input.Length)
            {
                Match match = regex.Match(input, position);
                Token token = new Token();
                token.LineNumber = lineNumber;
                token.ColumnNumber = columnNumber;

                //pokud regex nenamatchuje řetězec začínající na aktuální pozici, museli jsme narazit
                //na něco, co není popsáno v definicích tokenů a oznámíme to výjimkou
                if (!match.Success || (match.Index > position))
                {
                    string unexpectedCharacter = null;
                    switch (input[position])
                    {
                        case '\n':
                            unexpectedCharacter = "\\n";
                            break;
                        case '\r':
                            unexpectedCharacter = "\\r";
                            break;
                        case '\t':
                            unexpectedCharacter = "\\t";
                            break;
                        default:
                            unexpectedCharacter = input[position].ToString();
                            break;
                    }
                    throw new ParsingException(string.Format("Unexpected character '{0}' encountered by the lexer.",
                                                             unexpectedCharacter), lineNumber, columnNumber);
                }

                //regex obsahuje capture groupy s názvy __0, __1..., groupy jsou seřazeny tak, že ty s nižším
                //číslem mají přednost (dokumentovaná vlastnost regexího operátoru | )
                //když u nějaké groupy najdeme match, podíváme se do groupSymbolCodes, o jaký terminál se jedná
                for (int i = 0; i < groupSymbolCodes.Count; i++)
                {
                    if (match.Groups["__" + i.ToString()].Success)
                    {
                        token.SymbolCode = groupSymbolCodes[i];
                        token.Value = match.Value;
                        break;
                    }
                }

                position += match.Length;

                //přepočítáme "lidskou" adresu v textu (namatchované řetězce můžou být víceřádkové)
                int lastNewLine = match.Value.LastIndexOf('\n');
                if (lastNewLine == -1)
                {
                    columnNumber += match.Length;
                }
                else
                {
                    columnNumber = match.Length - lastNewLine;
                    foreach (char c in match.Value)
                        if (c == '\n')
                            lineNumber++;
                }

                if (token.SymbolCode == -1)
                    return GetNextToken();
                else
                    return token;
            }
            else
            {
                Token finalToken = new Token();
                finalToken.LineNumber = lineNumber;
                finalToken.ColumnNumber = columnNumber;
                //token == $end
                finalToken.SymbolCode = 0;
                finalToken.Value = "";

                hasTokens = false;

                return finalToken;
            }
        }

        /// <summary>
        /// Gets whether the remainder of the source string contains any more tokens.
        /// (including the final $end symbol)
        /// </summary>
        public bool HasTokens
        { get { return hasTokens; } }

        /// <summary>
        /// Gets or sets the string from which tokens are read.
        /// </summary>
        public string SourceString
        {
            get
            {
                return input.Substring(position);
            }
            set
            {
                input = value;
                hasTokens = true;
                position = 0;
                lineNumber = 1;
                columnNumber = 1;
            }
        }
    }
    
    /// <summary>
    /// Fetches the next token from the source string.
    /// </summary>
    /// <returns>The next token discovered in the input string.</returns>
	public class GrammarLexer : ILexer
	{
        int position = 0, lineNumber = 1, columnNumber = 1;
        string input = "";
        bool hasTokens = false;
		private int previousSymbolCode = GrammarParser.CODE_SKIP;
		
		// Regular expressions to help with the tokenization of the grammar
		// specification.
		private static readonly Regex re_identifier = new Regex("\\w+"),
									  re_whitespace = new Regex("\\s+"),
									  re_regexOpts = new Regex("\\(\\?[imnsx\\-]+\\)"),
									  re_endOfHeader = new Regex("^\\s*%lexer\\b", RegexOptions.Multiline),
								      re_singleQuoteContents = new Regex("([^\\\\']|\\.)*"),
								      re_doubleQuoteContents = new Regex("([^\\\\\"]|\\\\.)*");
		
		
		private void reportParsingError(string message) {
			throw new ParsingException(message, this.lineNumber, this.columnNumber);
		}
        
        /// <summary>
        /// Fetches the next token from the source string.
        /// </summary>
        /// <returns>The next token discovered in the input string.</returns>
        public Token GetNextToken()
        {
            if (position < input.Length)
            {
            	int matchLength = 0;
            	int symbolCode = -2;
            	int endOfLine;
            	
            	// HEADER_CODE, the code that is to be put at the beginning of the
				// generated source file
	        	if (previousSymbolCode == GrammarParser.CODE_HEADER) {
	        		symbolCode = GrammarParser.CODE_HEADERCODE;
	        		var endOfHeaderMatch = re_endOfHeader.Match(input, position);
					if (!endOfHeaderMatch.Success)
						matchLength = input.Length - position;
					else
						matchLength = endOfHeaderMatch.Index - position;
				// REGEX, a regular expression defining a token
				} else if (previousSymbolCode == GrammarParser.CODE_EQUALS) {
					symbolCode = GrammarParser.CODE_REGEX;
					endOfLine = input.IndexOf('\n', position);
					if (endOfLine == -1)
						matchLength = input.Length - position;
					else
						matchLength = endOfLine - position;
				} else switch (input[position]) {
				case '<':
					symbolCode = GrammarParser.CODE_LANGLE;
					matchLength = 1;
					break;
				case '>':
					symbolCode = GrammarParser.CODE_RANGLE;
					matchLength = 1;
					break;
				case '.':
					symbolCode = GrammarParser.CODE_DOT;
					matchLength = 1;
					break;
				case '|':
					symbolCode = GrammarParser.CODE_OR;
					matchLength = 1;
					break;
				case '=':
					symbolCode = GrammarParser.CODE_EQUALS;
					matchLength = 1;
					break;
				case ':':
					if ((input[position + 1] == ':') && (input[position + 2] == '=')) {
						symbolCode = GrammarParser.CODE_DERIVES;
						matchLength = 3;
					} else {
						reportParsingError(string.Format("Unexpected character '{0}' following ':'.", input[position + 1]));
					}
					break;
                case '"':
                    symbolCode = GrammarParser.CODE_QUOTED;
                    int endOfQuotes = input.IndexOf('"', position + 1);
                    if (endOfQuotes == -1)
                        matchLength = input.Length - position;
                    else
                        matchLength = (endOfQuotes + 1) - position;
                    break;
				case '(':
					var match = re_regexOpts.Match(input, position);
					if (match.Index == position) {
						symbolCode = GrammarParser.CODE_REGEXOPTS;
						matchLength = match.Length;
					} else {
						reportParsingError(string.Format("\"{0}\" is not a proper regular expression option setter."));
					}
					break;
				case '#':
					endOfLine = input.IndexOf('\n', position + 1);
					symbolCode = GrammarParser.CODE_SKIP;
					if (endOfLine == -1)
						matchLength = input.Length - position;
					else
						matchLength = (endOfLine + 1) - position;
					break;
				case '%':
					var keyword = re_identifier.Match(input, position + 1);
					switch (keyword.Value) {
					case "header":
						symbolCode = GrammarParser.CODE_HEADER;
						break;
					case "lexer":
						symbolCode = GrammarParser.CODE_LEXER;
						break;
					case "null":
						symbolCode = GrammarParser.CODE_NULL;
						break;
					case "parser":
						symbolCode = GrammarParser.CODE_PARSER;
						break;
					case "start":
						symbolCode = GrammarParser.CODE_START;
						break;
					case "type":
						symbolCode = GrammarParser.CODE_TYPE;
						break;
					case "userobject":
						symbolCode = GrammarParser.CODE_USEROBJECT;
						break;
					default:
						reportParsingError(string.Format("Unknown keyword \"%{0}\".", keyword.Value));
						break;
					}
					matchLength = 1 + keyword.Length;
					break;
				case '{':
					int blockDepth = 1;
					int blockPosition = position + 1;
					while ((blockDepth > 0) && (blockPosition < input.Length)) {
						switch (input[blockPosition]) {
						case '{':
							blockDepth++;
							blockPosition++;
							break;
						case '}':
							blockDepth--;
							blockPosition++;
							break;
						case '/':
							if (input[blockPosition + 1] == '/') {
								endOfLine = input.IndexOf('\n', blockPosition + 2);
								if (endOfLine == -1) {
									blockPosition = input.Length;
								} else {
									blockPosition = endOfLine + 1;
								}
							} else if (input[blockPosition + 1] == '*') {
								int endOfComment = input.IndexOf("*/", blockPosition + 2);
								if (endOfComment == -1) {
									blockPosition = input.Length;
								} else {
									blockPosition = endOfComment + 2;
								}
							} else {
								blockPosition++;
							}
							break;
						case '\'':
						case '"':
							char quote = input[blockPosition];
							Regex quoteContents = quote == '"' ? re_doubleQuoteContents : re_singleQuoteContents;
							int literalLength = quoteContents.Match(input, blockPosition + 1).Length;
							blockPosition += 1 + literalLength + 1;
							break;
						default:
							blockPosition++;
							break;
						}
					}
					symbolCode = GrammarParser.CODE_CODE;
					matchLength = blockPosition - position;
					break;
				default:
					var whitespace_match = re_whitespace.Match(input, position);
					if (whitespace_match.Index == position) {
						symbolCode = GrammarParser.CODE_SKIP;
						matchLength = whitespace_match.Length;
					} else {
						var identifier_match = re_identifier.Match(input, position);
						if (identifier_match.Index == position) {
							symbolCode = GrammarParser.CODE_IDENTIFIER;
							matchLength = identifier_match.Length;
						} else {
							reportParsingError(string.Format("Unexpected character '{0}'.", input[position]));
						}
					}
					break;
				}
				
                Token token = new Token();
                token.LineNumber = lineNumber;
                token.ColumnNumber = columnNumber;
                token.SymbolCode = symbolCode;
				token.Value = input.Substring(position, matchLength);
 

                //přepočítáme "lidskou" adresu v textu (namatchované řetězce můžou být víceřádkové)
                int lastNewLine = token.Value.LastIndexOf('\n');
                if (lastNewLine == -1)
                {
                    columnNumber += matchLength;
                }
                else
                {
                    columnNumber = token.Value.Length - lastNewLine;
                    foreach (char c in token.Value)
                        if (c == '\n')
                            lineNumber++;
                }
                
                position += matchLength;
				previousSymbolCode = symbolCode;
				
                if (token.SymbolCode == -1) {
                    return GetNextToken();
				} else {
                    return token;
				}
            }
            else
            {
                Token finalToken = new Token();
                finalToken.LineNumber = lineNumber;
                finalToken.ColumnNumber = columnNumber;
                //token == $end
                finalToken.SymbolCode = GrammarParser.CODE_END;
                finalToken.Value = "";

                hasTokens = false;

				previousSymbolCode = 0;
                return finalToken;
            }
        }

        /// <summary>
        /// Gets whether the remainder of the source string contains any more tokens.
        /// (including the final $end symbol)
        /// </summary>
        public bool HasTokens
        { get { return hasTokens; } }

        /// <summary>
        /// Gets or sets the string from which tokens are read.
        /// </summary>
        public string SourceString
        {
            get
            {
                return input.Substring(position);
            }
            set
            {
                input = value;
                hasTokens = true;
                position = 0;
                lineNumber = 1;
                columnNumber = 1;
                previousSymbolCode = GrammarParser.CODE_SKIP;
            }
        }
	}
}
