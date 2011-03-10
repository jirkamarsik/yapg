using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace YetAnotherParserGenerator
{
    /// <summary>
    /// A tree structure containing the result of successful parsing.
    /// </summary>
    [Serializable]
    public class ParseTree
    {
        private int lineNumber, columnNumber;
        private string symbolName, value;
        private ParseTree[] daughters;

        /// <summary>
        /// Creates a terminal tree node.
        /// </summary>
        /// <param name="symbolName">The name of the terminal.</param>
        /// <param name="value">The text value of the terminal.</param>
        /// <param name="lineNumber">The line at which the symbol's string begins.</param>
        /// <param name="columnNumber">The position within the line where the symbol's string begins.</param>
        public ParseTree(string symbolName, string value, int lineNumber, int columnNumber)
        {
            this.symbolName = symbolName;
            this.value = value;
            this.daughters = new ParseTree[0];
            this.lineNumber = lineNumber;
            this.columnNumber = columnNumber;
        }

        /// <summary>
        /// Creates a nonterminal tree node.
        /// </summary>
        /// <param name="symbolName">The name of the nonterminal.</param>
        /// <param name="daughters">A collection of the daughter nodes.</param>
        /// <param name="lineNumber">The line at which the symbol's string begins.</param>
        /// <param name="columnNumber">The position within the line where the symbol's string begins.</param>
        public ParseTree(string symbolName, IEnumerable<ParseTree> daughters, int lineNumber, int columnNumber)
        {
            this.symbolName = symbolName;
            this.daughters = daughters.ToArray();
            this.lineNumber = lineNumber;
            this.columnNumber = columnNumber;
        }

        /// <summary>
        /// Converts the ParseTree into XML form for easy and readable disk storage.
        /// </summary>
        /// <returns>An instance of XmlDocument describing the ParseTree's structure.</returns>
        public XmlDocument ConvertTreeToXml()
        {
            XmlDocument doc = new XmlDocument();

            doc.AppendChild(convertTreeNodeToXmlNode(this, doc));

            return doc;
        }

        /// <summary>
        /// Creates an XmlNode representation of a ParseTree node and it's subtree for a specified XmlDocument instance.
        /// </summary>
        /// <param name="treeNode">The TreeNode whose XML representation is to be constructed.</param>
        /// <param name="doc">The XmlDocument instance under which XmlNode instances are to be registered.</param>
        /// <returns>The XmlNode representing the <i>treeNode</i></returns>
        private XmlNode convertTreeNodeToXmlNode(ParseTree treeNode, XmlDocument doc)
        {
            XmlNode newNode = doc.CreateElement(treeNode.SymbolName);

            newNode.InnerText = treeNode.Value;
            foreach (ParseTree daughter in treeNode.Daughters)
                newNode.AppendChild(convertTreeNodeToXmlNode(daughter, doc));

            return newNode;
        }

        /// <summary>
        /// Writes a hopefully human-friendly representation of the ParseTree in the specified TextWriter.
        /// </summary>
        /// <param name="writer">The TextWriter instance to which the tree representation will be written.</param>
        public void DrawAsciiTree(TextWriter writer)
        {
            drawAsciiTreeNode(this, 0, writer);
        }

        /// <summary>
        /// Draws a portion of the ParseTree.
        /// </summary>
        /// <param name="node">The ParseTree whose subtree is to be rendered.</param>
        /// <param name="depth">The depth of the <i>node</i> in the resulting tree.</param>
        /// <param name="writer">The TextWriter to which the tree will be written.</param>
        private void drawAsciiTreeNode(ParseTree node, int depth, TextWriter writer)
        {
            for (int i = 0; i < depth; i++)
                if (i < depth - 1)
                    writer.Write("|   ");
                else
                    writer.Write("|---");

            if (node.Value != null)
                writer.WriteLine("\"" + node.Value + "\"");
            else
            {
                writer.WriteLine(node.SymbolName);

                foreach (ParseTree daughter in node.Daughters)
                    drawAsciiTreeNode(daughter, depth + 1, writer);
            }
        }

        /// <summary>
        /// Gets the name of the symbol represented by this node.
        /// </summary>
        public string SymbolName
        { get { return symbolName; } }

        /// <summary>
        /// Gets the text value of the node's terminal symbol. Null if the node represents a nonterminal.
        /// </summary>
        public string Value
        { get { return value; } }

        /// <summary>
        /// Gets the array of the node's daughters. Empty if the node represents a terminal.
        /// </summary>
        public ParseTree[] Daughters
        { get { return daughters; } }

        /// <summary>
        /// Gets the number of the line where the string in this subtree begins.
        /// </summary>
        public int LineNumber
        { get { return lineNumber; } }

        /// <summary>
        /// Gets the position within the line where the string in this subtree begins.
        /// </summary>
        public int ColumnNumber
        { get { return columnNumber; } }
    }
}