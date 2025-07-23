using Microsoft.Extensions.Logging;
using MediatR;
using Moq;
using Toko.Models;
using Toko.Services;
using Toko.Shared.Models;
using Toko.Shared.Services;
using Xunit;
using Xunit.Abstractions;
using static Toko.Shared.Models.RaceMap;
using static Toko.Shared.Services.RaceMapFactory;

namespace Toko.IntegrationTests
{
    public class TurnExecutorTests
    {
        private readonly ITestOutputHelper _output;
        
        public TurnExecutor? TurnExecutor { get; set; }
        public Racer? Racer { get; set; }
        public Room? Room { get; set; }
        private int _stepCounter = 0;

        public TurnExecutorTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private void WriteTestOutput(string message)
        {
            _output.WriteLine(message);
        }

        private void WriteMapDebugInfo(RaceMap map)
        {
            WriteTestOutput("=== Map Structure Debug Info ===");
            for (int segIndex = 0; segIndex < map.Segments.Count; segIndex++)
            {
                var segment = map.Segments[segIndex];
                WriteTestOutput($"Segment {segIndex}: Direction={segment.Direction}, LaneCount={segment.LaneCount}, CellCount={segment.CellCount}, IsIntermediate={segment.IsIntermediate}");
            }
            WriteTestOutput("=================================");
        }

        private void SetupTestEnvironment(RaceMap map)
        {
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

            _stepCounter = 0; // Reset step counter

            WriteMapDebugInfo(map);
        }

        private ConcreteInstruction CreateMoveInstruction(int effect = 1)
        {
            return new ConcreteInstruction
            {
                Type = CardType.ShiftGear,
                ExecParameter = new ExecParameter { Effect = effect }
            };
        }

        [Fact]
        public async Task MoveForward_Should_Update_Racer_Position_Correctly()
        {
            // Setup map with simple 4-segment circular track
            List<MapSegmentSnapshot> mapSegmentSnapshots = new List<MapSegmentSnapshot>
            {
                new MapSegmentSnapshot(CellType.Road.ToString(), 2, 3, SegmentDirection.Up.ToString(), false),
                new MapSegmentSnapshot(CellType.Road.ToString(), 2, 3, SegmentDirection.Right.ToString(), false),
                new MapSegmentSnapshot(CellType.Road.ToString(), 2, 3, SegmentDirection.Down.ToString(), false),
                new MapSegmentSnapshot(CellType.Road.ToString(), 2, 3, SegmentDirection.Left.ToString(), false)
            };
            var map = CreateMap(mapSegmentSnapshots);
            SetupTestEnvironment(map);

            var instruction = CreateMoveInstruction();

            // Test movement through the track
            var expectedPositions = new[]
            {
                (0, 0, 1), (0, 0, 2),           // Move forward in first segment
                (1, 0, 0), (1, 1, 1),           // Enter curve, drive around
                (2, 1, 0), (2, 1, 1), (2, 1, 2), // Move through second segment
                (3, 1, 0), (3, 0, 1),           // Enter next curve
                (4, 1, 0), (4, 1, 1), (4, 1, 2), // Continue through track
                (5, 1, 0), (5, 0, 1),           // Another curve
                (6, 0, 0), (6, 0, 1), (6, 0, 2), // Move through segment
                (7, 0, 0), (7, 1, 1)            // Final movements
            };

            foreach (var (expectedSegment, expectedLane, expectedCell) in expectedPositions)
            {
                AssertRacerPositionAfterInstruction(instruction, expectedSegment, expectedLane, expectedCell);
            }

            if (Room != null)
                await Room.DisposeAsync();
        }

        [Fact]
        public async Task MoveForward_Should_Update_Racer_Position_Correctly2()
        {
            // Setup map with simple 4-segment circular track
            List<MapSegmentSnapshot> mapSegmentSnapshots = new List<MapSegmentSnapshot>
            {
                new MapSegmentSnapshot(CellType.Road.ToString(), 2, 3, SegmentDirection.Up.ToString(), false),
                new MapSegmentSnapshot(CellType.Road.ToString(), 2, 3, SegmentDirection.Right.ToString(), false),
                new MapSegmentSnapshot(CellType.Road.ToString(), 2, 3, SegmentDirection.Down.ToString(), false),
                new MapSegmentSnapshot(CellType.Road.ToString(), 2, 3, SegmentDirection.Left.ToString(), false)
            };
            var map = CreateMap(mapSegmentSnapshots);
            SetupTestEnvironment(map);

            var changeLaneInstruction = new ConcreteInstruction
            {
                Type = CardType.ChangeLane,
                ExecParameter = new ExecParameter { Effect = 1 } // Change to right lane
            };
            var changeLaneResult = TurnExecutor!.ApplyInstruction(Racer!, changeLaneInstruction, Room!, new List<INotification>());
            AssertRacerPositionAfterInstruction(changeLaneInstruction, 0, 1, 0); // Expect to change to right lane in first segment


            var instruction = CreateMoveInstruction();

            // Test movement through the track
            var expectedPositions = new[]
            {
                (0, 1, 1), (0, 1, 2),           // Move forward in first segment
                (1, 1, 0),           // Enter curve, drive around
                (2, 0, 0), (2, 0, 1), (2, 0, 2), // Move through second segment
                (3, 0, 0),         // Enter next curve
                (4, 0, 0), (4, 0, 1), (4, 0, 2), // Continue through track
                (5, 0, 0),          // Another curve
                (6, 1, 0), (6, 1, 1), (6, 1, 2), // Move through segment
                (7, 1, 0)         // Final movements
            };

            foreach (var (expectedSegment, expectedLane, expectedCell) in expectedPositions)
            {
                AssertRacerPositionAfterInstruction(instruction, expectedSegment, expectedLane, expectedCell);
            }

            if (Room != null)
                await Room.DisposeAsync();
        }
        
        [Fact]
        public async Task MoveForward_Should_Update_Racer_Position_Correctly3()
        {
            // Setup map with more complex track segments
            var segments = new List<TrackSegment>
            {
                CreateNormalSegment(CellType.Road, 2, 6, SegmentDirection.Up),
                CreateNormalSegment(CellType.Road, 2, 1, SegmentDirection.Right),
                CreateNormalSegment(CellType.Road, 2, 3, SegmentDirection.Down),
                CreateNormalSegment(CellType.Road, 2, 1, SegmentDirection.Right),
                CreateNormalSegment(CellType.Road, 2, 3, SegmentDirection.Up),
                CreateNormalSegment(CellType.Road, 2, 1, SegmentDirection.Right),
                CreateNormalSegment(CellType.Road, 2, 6, SegmentDirection.Down),
                CreateNormalSegment(CellType.Road, 2, 7, SegmentDirection.Left),
            };
            var map = GenerateFinalMapWithIntermediate(segments);
            SetupTestEnvironment(map);

            var instruction = CreateMoveInstruction();

            // Test movement through the complex track
            var expectedPositions = new[]
            {
                // Initial segment (6 cells)
                (0, 0, 1), (0, 0, 2), (0, 0, 3), (0, 0, 4), (0, 0, 5),
                // Enter curve and continue
                (1, 0, 0), (1, 1, 1),
                (2, 1, 0),
                (3, 1, 0), (3, 0, 1),
                (4, 1, 0), (4, 1, 1), (4, 1, 2),
                (5, 1, 0),
                (6, 1, 0),
                (7, 1, 0),
                (8, 0, 0), (8, 0, 1), (8, 0, 2),
                // Continue through remaining segments
                (9, 0, 0), (9, 1, 1),
                (10, 1, 0),
                (11, 1, 0), (11, 0, 1),
                (12, 1, 0), (12, 1, 1), (12, 1, 2), (12, 1, 3), (12, 1, 4), (12, 1, 5),
                (13, 1, 0), (13, 0, 1),
                (14, 0, 0), (14, 0, 1), (14, 0, 2), (14, 0, 3), (14, 0, 4), (14, 0, 5), (14, 0, 6),
                (15, 0, 0), (15, 1, 1)
            };

            foreach (var (expectedSegment, expectedLane, expectedCell) in expectedPositions)
            {
                AssertRacerPositionAfterInstruction(instruction, expectedSegment, expectedLane, expectedCell);
            }

            if (Room != null)
                await Room.DisposeAsync();
        }

        [Fact]
        public async Task MoveForward_Should_Update_Racer_Position_Correctly4()
        {
            // Setup map with more complex track segments
            var segments = new List<TrackSegment>
            {
                CreateNormalSegment(CellType.Road, 2, 6, SegmentDirection.Up),
                CreateNormalSegment(CellType.Road, 2, 1, SegmentDirection.Right),
                CreateNormalSegment(CellType.Road, 2, 3, SegmentDirection.Down),
                CreateNormalSegment(CellType.Road, 2, 1, SegmentDirection.Right),
                CreateNormalSegment(CellType.Road, 2, 3, SegmentDirection.Up),
                CreateNormalSegment(CellType.Road, 2, 1, SegmentDirection.Right),
                CreateNormalSegment(CellType.Road, 2, 6, SegmentDirection.Down),
                CreateNormalSegment(CellType.Road, 2, 7, SegmentDirection.Left),
            };
            var map = GenerateFinalMapWithIntermediate(segments);
            SetupTestEnvironment(map);

            var instruction = CreateMoveInstruction();
            var changeLaneInstruction = new ConcreteInstruction
            {
                Type = CardType.ChangeLane,
                ExecParameter = new ExecParameter { Effect = 1 } // Change to right lane
            };
            AssertRacerPositionAfterInstruction(changeLaneInstruction, 0, 1, 0); // Expect to change to right lane in first segment
            // Test movement through the complex track
            var expectedPositions = new[]
            {
                // Initial segment (6 cells)
                (0, 1, 1), (0, 1, 2), (0, 1, 3), (0, 1, 4), (0, 1, 5),
                // Enter curve and continue
                (1, 1, 0),
                (2, 0, 0),
                (3, 0, 0),
                (4, 0, 0), (4, 0, 1), (4, 0, 2),
                (5, 0, 0), (5, 1, 1),
                (6, 0, 0),
                (7, 0, 0), (7, 1, 1),
                (8, 1, 0), (8, 1, 1), (8, 1, 2),
                // Continue through remaining segments
                (9, 1, 0), 
                (10, 0, 0),
                (11, 0, 0),
                (12, 0, 0), (12, 0, 1), (12, 0, 2), (12, 0, 3), (12, 0, 4), (12, 0, 5),
                (13, 0, 0),
                (14, 1, 0), (14, 1, 1), (14, 1, 2), (14, 1, 3), (14, 1, 4), (14, 1, 5), (14, 1, 6),
                (15, 1, 0)
            };

            foreach (var (expectedSegment, expectedLane, expectedCell) in expectedPositions)
            {
                AssertRacerPositionAfterInstruction(instruction, expectedSegment, expectedLane, expectedCell);
            }

            if (Room != null)
                await Room.DisposeAsync();
        }

        [Fact]
        public async Task MoveForward_Should_Update_Racer_Position_Correctly5()
        {
            // Setup map with more complex track segments
            var segments = new List<TrackSegment>
            {
                CreateNormalSegment(CellType.Road, 3, 3, SegmentDirection.Up),
                CreateNormalSegment(CellType.Road, 3, 3, SegmentDirection.Right),
                CreateNormalSegment(CellType.Road, 3, 3, SegmentDirection.Down),
                CreateNormalSegment(CellType.Road, 3, 3, SegmentDirection.Left),
            };
            var map = GenerateFinalMapWithIntermediate(segments);
            SetupTestEnvironment(map);

            var instruction = CreateMoveInstruction();
            var changeLaneInstruction = new ConcreteInstruction
            {
                Type = CardType.ChangeLane,
                ExecParameter = new ExecParameter { Effect = 1 } // Change to right lane
            };
            AssertRacerPositionAfterInstruction(changeLaneInstruction, 0, 1, 0); // Expect to change to right lane in first segment
            // Test movement through the complex track
            var expectedPositions = new[]
            {
                // Initial segment (6 cells)
                (0, 1, 1), (0, 1, 2),
                // Enter curve and continue
                (1, 1, 0), (1, 1, 1), (1, 2, 1),
                (2, 1, 0), (2, 1, 1), (2, 1, 2),
                (3, 1, 0), (3, 1, 1), (3, 0, 1),
                (4, 1, 0), (4, 1, 1), (4, 1, 2),
                (5, 1, 0), (5, 1, 1), (5, 0, 1),
                (6, 1, 0), (6, 1, 1), (6, 1, 2),
                (7, 1, 0), (7, 1, 1), (7, 2, 1),
            };

            foreach (var (expectedSegment, expectedLane, expectedCell) in expectedPositions)
            {
                AssertRacerPositionAfterInstruction(instruction, expectedSegment, expectedLane, expectedCell);
            }

            if (Room != null)
                await Room.DisposeAsync();
        }
        private void AssertRacerPositionAfterInstruction(ConcreteInstruction instruction, int expectedSegmentIndex, int expectedLaneIndex, int expectedCellIndex)
        {
            _stepCounter++;
            var result = TurnExecutor!.ApplyInstruction(Racer!, instruction, Room!, new List<INotification>());

            Assert.True(result == TurnExecutor.TurnExecutionResult.Continue,
                $"Step {_stepCounter} execution failed - Expected: Continue, Actual: {result}");

            // Get actual coordinates
            var actualSegment = Room!.Map.Segments[Racer!.SegmentIndex];
            var actualCoord = actualSegment.LaneCells[Racer!.LaneIndex][Racer!.CellIndex].Position;
            
            // Get expected coordinates
            var expectedSegment = Room!.Map.Segments[expectedSegmentIndex];
            var expectedCoord = expectedSegment.LaneCells[expectedLaneIndex][expectedCellIndex].Position;

            try
            {
                Assert.Equal(expectedSegmentIndex, Racer!.SegmentIndex);
                Assert.Equal(expectedLaneIndex, Racer!.LaneIndex);
                Assert.Equal(expectedCellIndex, Racer!.CellIndex);

                // Output successful debug information with coordinates only
                WriteTestOutput($"Step {_stepCounter}: Coord({actualCoord.X}, {actualCoord.Y}) -> Expected Coord({expectedCoord.X}, {expectedCoord.Y}) ✓");
            }
            catch (Xunit.Sdk.EqualException ex)
            {
                // Output detailed failure information with coordinates only
                WriteTestOutput($"Step {_stepCounter} failed:");
                WriteTestOutput($"  Coord({actualCoord.X}, {actualCoord.Y})");
                WriteTestOutput($"  Expected: Coord({expectedCoord.X}, {expectedCoord.Y}) ✗");
                throw new Exception($"Step {_stepCounter} assertion failed - {ex.Message}", ex);
            }
        }
    }
}