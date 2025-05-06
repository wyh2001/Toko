using Toko.Models;

namespace Toko.Services
{
    public class TurnExecutor
    {
        public void ExecuteTurn(Room room)
        {
            foreach (var racer in room.Racers)
            {
                if (!room.SubmittedInstructions.TryGetValue(racer.Id, out var instructions))
                    continue;

                foreach (var instruction in instructions)
                {
                    ApplyInstruction(racer, instruction);
                }

                room.SubmittedInstructions[racer.Id] = new();
            }
        }

        private void ApplyInstruction(Racer racer, InstructionType ins)
        {
            switch (ins)
            {
                case InstructionType.Accelerate:
                    racer.Speed += 1;
                    break;
                case InstructionType.Decelerate:
                    racer.Speed = Math.Max(0, racer.Speed - 1);
                    break;
                case InstructionType.Left:
                    racer.Facing = TurnLeft(racer.Facing);
                    break;
                case InstructionType.Right:
                    racer.Facing = TurnRight(racer.Facing);
                    break;
                case InstructionType.Forward:
                    MoveForward(racer);
                    break;
                case InstructionType.DriftLeft:
                    racer.Facing = TurnLeft(racer.Facing);
                    MoveForward(racer);
                    break;
                case InstructionType.DriftRight:
                    racer.Facing = TurnRight(racer.Facing);
                    MoveForward(racer);
                    break;
                case InstructionType.UseItem:
                    // TODO:
                    break;
                default:
                    break;
            }
        }

        private void MoveForward(Racer racer)
        {
            for (int i = 0; i < racer.Speed; i++)
            {
                switch (racer.Facing)
                {
                    case Direction.Up: racer.Y -= 1; break;
                    case Direction.Down: racer.Y += 1; break;
                    case Direction.Left: racer.X -= 1; break;
                    case Direction.Right: racer.X += 1; break;
                }
            }
        }

        private Direction TurnLeft(Direction d) =>
            (Direction)(((int)d + 3) % 4);

        private Direction TurnRight(Direction d) =>
            (Direction)(((int)d + 1) % 4);
    }
}
