using System;

namespace WinAI.Models
{
    /// <summary>
    /// Flags representing capabilities supported by an AI model.
    /// Extensible for multimodal vision, reasoning, streaming, and tool use.
    /// </summary>
    [Flags]
    public enum ModelCapabilities
    {
        None = 0,
        Text = 1,
        Vision = 2,
        Streaming = 4,
        Reasoning = 8,
        ToolUse = 16
    }
}
