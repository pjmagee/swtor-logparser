﻿using System.Globalization;

namespace SwtorLogParser;

public class Actor
{
    private List<ReadOnlyMemory<char>> Roms { get; }
    private bool IsEmpty => Roms.Count == 0 || (Roms.Count == 1 && Roms[0].Length == 1 && Roms[0].Span[0] == '=');
	
    public string? Name
    {
        get 
        {
            try 
            {
                if (IsCompanion)
                    return Roms[0].Span.Slice(Roms[0].Span.IndexOf('/') + 1, Roms[0].Span.IndexOf('{') - 1 - Roms[0].Span.IndexOf('/')).Trim().ToString();
                
                if (IsPlayer)
                    return Roms[0].Span.Slice(1, Roms[0].Span.IndexOf('#') - 1).Trim().ToString();
					
                return Roms[0].Span.Slice(0, Roms[0].Span.IndexOf('{')).Trim().ToString();
            }
            catch
            {
                return null;
            }
        }
    }
	
    public bool IsNpc => !IsEmpty && Roms[0].Span[0] != '@';
    public bool IsPlayer => Roms.Count > 0 && Roms[0].Span.Length > 0 && Roms[0].Span[0] == '@';
    public bool IsCompanion => IsPlayer && Roms[0].Span.IndexOf('/') > 0;
	
    public int? Health
    {
        get
        {
            if (Roms.Count == 3)
            {
                return int.Parse(Roms[2].Slice(1, Roms[2].Span.IndexOf('/') - 1).Span);
            }
			
            return null;
        }
    }

    public int? MaxHealth
    {
        get
        {
            if (!IsEmpty)
            {
                var health = Roms[2].Span;
                int maxStart = health.IndexOf('/') + 1;
                int maxLength = health.Length - maxStart - 1;
                return int.Parse(Roms[2].Slice(maxStart, maxLength).Span);
            }
			
            return null;
        }
    }
	
    public (float X, float Y, float Z, float Direction)? Position
    {
        get
        {
            if (Roms.Count == 3)
            {
                var position = ExtractPosition(Roms[1].Span);
                return (position[0], position[1], position[2], position[3]);
            }
			
            return null;			
        }
    }
	
    public long? Id
    {
        get 
        {
            if (IsPlayer)
            {
                var hash = Roms[0].Span.IndexOf('#');
                var slash = Roms[0].Span.IndexOf('/');

                if (IsCompanion)
                {
                    var idStart = Roms[0].Span.IndexOf('{');
                    var idEnd = Roms[0].Span.IndexOf('}');
                    return long.Parse(Roms[0].Span.Slice(idStart + 1, idEnd  - idStart - 1));
                }

                return long.Parse(Roms[0].Span.Slice(hash + 1, Roms[0].Span.Length - 1 - hash));
            }

            if (IsNpc)
            {
                int openIndex = Roms[0].Span.IndexOf('{');
                int closeIndex = Roms[0].Span.IndexOf('}');
                return long.Parse(Roms[0].Span.Slice(openIndex + 1, closeIndex - openIndex - 1));
            }

            return null;
        }
    }

    private Actor(ReadOnlyMemory<char> rom)
    {
        Roms = GetSubSections(rom);
    }

    public static Actor? Parse(ReadOnlyMemory<char> rom)
    {
        if (rom.IsEmpty || (rom.Length == 1 && rom.Span[0] == '=')) return null;
        return new Actor(rom);
    }
	
    public override string ToString() => $"{Name} {Id} ({Health}/{MaxHealth}";

    private static List<float> ExtractPosition(ReadOnlySpan<char> span)
    {
        List<float> position = new List<float>();
        int start = 0;
        int end = 0;

        // Skip the opening parenthesis
        if (span[0] == '(')
        {
            start = 1;
        }

        // Exclude the closing parenthesis
        if (span[^1] == ')')
        {
            end = span.Length - 1;
        }
        else
        {
            end = span.Length;
        }

        ReadOnlySpan<char> content = span.Slice(start, end - start);

        // Split the content by comma
        start = 0;
        
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == ',')
            {
                if (float.TryParse(content.Slice(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    position.Add(number);
                }
                start = i + 1;
            }
        }

        // Handle the last number

        if (float.TryParse(content.Slice(start, content.Length - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var lastNumber))
        {
            position.Add(lastNumber);
        }

        return position;
    }

    private static List<ReadOnlyMemory<char>> GetSubSections(ReadOnlyMemory<char> rom)
    {
        List<ReadOnlyMemory<char>> subSections = new List<ReadOnlyMemory<char>>();
        int start = 0;

        for (int i = 0; i < rom.Length; i++)
        {
            if (rom.Span[i] == '|')
            {
                if (i > start)
                {
                    subSections.Add(new ReadOnlyMemory<char>(rom.Slice(start, i - start).ToArray()));
                }
                start = i + 1;
            }
        }

        if (rom.Length > start)
        {
            subSections.Add(new ReadOnlyMemory<char>(rom.Slice(start, rom.Length - start).ToArray()));
        }

        return subSections;
    }
}