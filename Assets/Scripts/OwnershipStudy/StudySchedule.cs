using System;
using System.Collections.Generic;

namespace OwnershipStudy
{
    public enum StudyLayout { AgentOff, AgentOn }
    public enum StudyMode { AgentOff, AgentOn, Both }

    public sealed class StudyTrial
    {
        public int Target { get; }
        public int[] MugsAtPositions { get; }
        public int Position => Array.IndexOf(MugsAtPositions, Target);

        public StudyTrial(int target, int[] mugsAtPositions)
        {
            Target = target;
            MugsAtPositions = mugsAtPositions;
        }
    }

    public static class StudySchedule
    {
        public static List<StudyTrial> CreateTrials(int repetitions, int phase, Random random)
        {
            if (repetitions < 1 || phase < 0 || phase > 2)
                throw new ArgumentOutOfRangeException();
            var trials = new List<StudyTrial>(repetitions * 3);
            for (int repetition = 0; repetition < repetitions; repetition++)
            {
                for (int mug = 0; mug < 3; mug++)
                {
                    int slot = (mug + phase + repetition) % 3;
                    var positions = new int[3];
                    positions[slot] = mug;
                    int other = (mug + random.Next(1, 3)) % 3;
                    positions[(slot + 1) % 3] = other;
                    positions[(slot + 2) % 3] = 3 - mug - other;
                    trials.Add(new StudyTrial(mug, positions));
                }
            }
            Shuffle(trials, random);
            return trials;
        }

        // Each allocation cycle crosses both starting layouts with all three position phases.
        public static int[] CreateAllocationCycle(Random random)
        {
            var cells = new[] { 0, 1, 2, 3, 4, 5 };
            Shuffle(cells, random);
            return cells;
        }

        private static void Shuffle<T>(IList<T> items, Random random)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                T item = items[i];
                items[i] = items[j];
                items[j] = item;
            }
        }
    }
}
