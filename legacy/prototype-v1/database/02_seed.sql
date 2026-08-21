/* =====================================================================
   CampusTrack - demo seed data
   NOTE: the backend also auto-seeds an admin user on first run
         (admin / Admin@123). Password hashes below are placeholders --
         create real users through the API or portal.
   ===================================================================== */
USE CampusTrack;
GO

INSERT INTO Semesters (Name, StartDate, EndDate, IsCurrent)
VALUES ('Fall 2026', '2026-08-01', '2026-12-20', 1);

INSERT INTO SchoolClasses (Name) VALUES ('Grade 7'), ('Grade 8'), ('BSCS-1');

INSERT INTO Sections (ClassId, Name)
SELECT Id, 'A' FROM SchoolClasses UNION ALL
SELECT Id, 'B' FROM SchoolClasses;

INSERT INTO Rooms (Name, RoomType) VALUES
 ('Main Gate',        'Gate'),
 ('Room 101',         'Classroom'),
 ('Room 102',         'Classroom'),
 ('Physics Lab',      'Laboratory'),
 ('Computer Lab',     'Laboratory'),
 ('Library',          'Library'),
 ('Discussion Room 1','DiscussionRoom'),
 ('Auditorium',       'Auditorium');

/* Gate reader: 3 antennas. Room readers: 2 antennas. */
INSERT INTO RfidReaders (ReaderCode, RoomId, AntennaCount)
SELECT 'GATE-01', Id, 3 FROM Rooms WHERE Name = 'Main Gate';

INSERT INTO RfidReaders (ReaderCode, RoomId, AntennaCount)
SELECT 'RDR-' + CAST(Id AS NVARCHAR), Id, 2 FROM Rooms WHERE RoomType <> 'Gate';
GO
