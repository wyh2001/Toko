using Microsoft.Extensions.Logging;
using MediatR;
using Moq;
using Toko.Models;
using Toko.Services;
using Toko.Shared.Models;
using Toko.Shared.Services;
using Xunit;
using static Toko.Shared.Models.RaceMap;
using static Toko.Shared.Services.RaceMapFactory;

namespace Toko.IntegrationTests
{
    public class TurnExecutorTests
    {
        public TurnExecutor? TurnExecutor { get; set; }
        public Racer? Racer { get; set; }
        public Room? Room { get; set; }

        [Fact]
        public async Task MoveForward_Should_Update_Racer_Position_Correctly()
        {
            List<MapSegmentSnapshot> mapSegmentSnapshots = new List<MapSegmentSnapshot>
            {
                new MapSegmentSnapshot(CellType.Road.ToString(), 2, 3, SegmentDirection.Up.ToString(), false),
                new MapSegmentSnapshot(CellType.Road.ToString(), 2, 3, SegmentDirection.Right.ToString(), false),
                new MapSegmentSnapshot(CellType.Road.ToString(), 2, 3, SegmentDirection.Down.ToString(), false),
                new MapSegmentSnapshot(CellType.Road.ToString(), 2, 3, SegmentDirection.Left.ToString(), false)
            };
            var map = CreateMap(mapSegmentSnapshots);

            var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<TurnExecutor>();
            TurnExecutor = new TurnExecutor(map, logger);

            Racer = new Racer
            {
                Id = "test-racer",
                PlayerName = "Test Player",
                SegmentIndex = 0,
                LaneIndex = 0,
                CellIndex = 0
            };

            var mockMediator = new Mock<IMediator>();
            var roomLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<Room>();
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var stepsPerRound = new List<int> { 5 };

            Room = new Room(mockMediator.Object, stepsPerRound, roomLogger, loggerFactory)
            {
                Map = map
            };
            Room.Racers.Add(Racer);

            var instruction = new ConcreteInstruction
            {
                Type = CardType.Move,
                ExecParameter = new ExecParameter { Effect = 1 }
            };

            AssertRacerPositionAfterInstruction(instruction, 0, 0, 1);
            AssertRacerPositionAfterInstruction(instruction, 0, 0, 2);
            // enter the curve segment
            AssertRacerPositionAfterInstruction(instruction, 1, 0, 0);
            AssertRacerPositionAfterInstruction(instruction, 1, 0, 1);
            AssertRacerPositionAfterInstruction(instruction, 1, 1, 1); // drive around the curve

            AssertRacerPositionAfterInstruction(instruction, 2, 1, 0);
            AssertRacerPositionAfterInstruction(instruction, 2, 1, 1);
            AssertRacerPositionAfterInstruction(instruction, 2, 1, 2);

            AssertRacerPositionAfterInstruction(instruction, 3, 1, 0);
            AssertRacerPositionAfterInstruction(instruction, 3, 1, 1);
            AssertRacerPositionAfterInstruction(instruction, 3, 0, 1);

            AssertRacerPositionAfterInstruction(instruction, 4, 1, 0);
            AssertRacerPositionAfterInstruction(instruction, 4, 1, 1);
            AssertRacerPositionAfterInstruction(instruction, 4, 1, 2);

            AssertRacerPositionAfterInstruction(instruction, 5, 1, 0);
            AssertRacerPositionAfterInstruction(instruction, 5, 1, 1);
            AssertRacerPositionAfterInstruction(instruction, 5, 0, 1);

            AssertRacerPositionAfterInstruction(instruction, 6, 0, 0);
            AssertRacerPositionAfterInstruction(instruction, 6, 0, 1);
            AssertRacerPositionAfterInstruction(instruction, 6, 0, 2);

            AssertRacerPositionAfterInstruction(instruction, 7, 0, 0);
            AssertRacerPositionAfterInstruction(instruction, 7, 0, 1);
            AssertRacerPositionAfterInstruction(instruction, 7, 1, 1);


            await Room.DisposeAsync();
        }
        private void AssertRacerPositionAfterInstruction(ConcreteInstruction instruction, int expectedSegmentIndex, int expectedLaneIndex, int expectedCellIndex)
        {
            var result = TurnExecutor!.ApplyInstruction(Racer!, instruction, Room!);

            Assert.True(result == TurnExecutor.TurnExecutionResult.Continue, 
                $"Execution failed - Expected: Continue, Actual: {result}, Position: ({Racer!.SegmentIndex}, {Racer!.LaneIndex}, {Racer!.CellIndex})");
            
            Assert.Equal(expectedSegmentIndex, Racer!.SegmentIndex);
            Assert.Equal(expectedLaneIndex, Racer!.LaneIndex);
            Assert.Equal(expectedCellIndex, Racer!.CellIndex);

        }
    }
}