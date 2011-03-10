using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace YetAnotherParserGenerator.Utilities
{
    //převzato od Patricka Caldwella
    /// <summary>
    /// A TextWriter that relays it's input to several other TextWriters.
    /// </summary>
    public class TextWriterProxy : TextWriter
    {
        private List<TextWriter> writers;

        /// <summary>
        /// Creates a new instance of TextWriterProxy with no listening TextWriters.
        /// </summary>
        public TextWriterProxy()
        {
            writers = new List<TextWriter>();
        }

        /// <summary>
        /// Registers a TextWriter which will receive input sent to this instance of TextWriterProxy.
        /// </summary>
        /// <param name="writer">The TextWriter to be added to the collection of listening TextWriters.</param>
        public void Add(TextWriter writer)
        {
            if (!writers.Contains(writer))
                writers.Add(writer);
        }

        /// <summary>
        /// Unregisters a previously registered TextWriter.
        /// </summary>
        /// <param name="writer">The TextWriter to be removed from the collection of listening TextWriters.</param>
        /// <returns><b>true</b> if <i>writer</i> was successfully unregistered, <b>false</b> otherwise</returns>
        public bool Remove(TextWriter writer)
        {
            return writers.Remove(writer);
        }

        /// <summary>
        /// Writes a character to the text stream of all registered TextWriters.
        /// </summary>
        /// <param name="value">The character to write to the text streams.</param>
        public override void Write(char value)
        {
            foreach (TextWriter writer in writers)
                writer.Write(value);

            base.Write(value);
        }

        /// <summary>
        /// Clears the buffers of all registered TextWriters and causes all buffered data
        /// to be written to underlying devices.
        /// </summary>
        public override void Flush()
        {
            foreach (TextWriter writer in writers)
                writer.Flush();

            base.Flush();
        }

        /// <summary>
        /// Closes the TextWriter instances registered in the TextWriterProxy as well as the TextWriterProxy
        /// instance as well releasing any system resources associated with them.
        /// </summary>
        public override void Close()
        {
            foreach (TextWriter writer in writers)
                writer.Close();

            base.Close();
        }

        /// <summary>
        /// Releases all resources used by the TextWriterProxy object and it's registered writers.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources</param>
        protected override void Dispose(bool disposing)
        {
            foreach (TextWriter writer in writers)
                writer.Dispose();

            base.Dispose(disposing);
        }

        //povinné
        /// <summary>
        /// Always returns Encoding.Default regardless of the encodings used in the registered TextWriters.
        /// </summary>
        public override Encoding Encoding
        { get { return Encoding.Default; } }

        /// <summary>
        /// Gets or sets the line terminator used by the writers registered.
        /// </summary>
        public override string NewLine
        {
            get { return base.NewLine; }
            set
            {
                foreach (TextWriter writer in writers)
                    writer.NewLine = value;

                base.NewLine = value;
            }
        }
        
        /// <summary>
        /// Gets where any TextWriters are registered to this TextWriterProxy.
        /// </summary>
        public bool HasAudience
        { get { return (writers.Count > 0); } }
    }
}
