-- 1. GEOGRAPHY
-- ==========================================

INSERT INTO Regions (Name) VALUES 
('Lviv Oblast'), ('Kyiv City'), ('Kyiv Oblast'), ('Kharkiv Oblast'), 
('Dnipropetrovsk Oblast'), ('Odessa Oblast'), ('Donetsk Oblast'), ('Zakarpattia Oblast');

INSERT INTO Cities (Id, RegionId, Name) VALUES 
(gen_random_uuid(), 1, 'Lviv'),
(gen_random_uuid(), 2, 'Kyiv'),
(gen_random_uuid(), 3, 'Brovary'),
(gen_random_uuid(), 4, 'Kharkiv'),
(gen_random_uuid(), 5, 'Dnipro'),
(gen_random_uuid(), 6, 'Odessa'),
(gen_random_uuid(), 7, 'Mariupol'),
(gen_random_uuid(), 7, 'Kramatorsk'),
(gen_random_uuid(), 8, 'Uzhhorod');

-- ==========================================
-- 2. SPORT DEFINITION (Using a variable to ensure ID consistency)
-- ==========================================

DO $$ 
DECLARE 
    sport_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f5f';
BEGIN
    INSERT INTO Sports (Id, Name) VALUES (sport_id, 'Water Polo');

    INSERT INTO PlayerPositionDefinitions (Id, SportId, Name, ShortName) VALUES 
    (gen_random_uuid(), sport_id, 'Goalkeeper', 'GK'),
    (gen_random_uuid(), sport_id, 'Center Forward', 'CF'),
    (gen_random_uuid(), sport_id, 'Center Back', 'CB'),
    (gen_random_uuid(), sport_id, 'Driver', 'D'),
    (gen_random_uuid(), sport_id, 'Wing', 'W'),
    (gen_random_uuid(), sport_id, 'Utility', 'UTL');

    INSERT INTO SportConfigurations (Id, SportId, UsesCleanTime, PeriodsCount, PeriodDurationMinutes, FieldSize, RosterLimit, LineupLimit)
    VALUES (gen_random_uuid(), sport_id, true, 4, 8, '25x20m', 15, 13);

    -- 3. TECHNICAL & TACTICAL ACTIONS
    INSERT INTO EventDefinitions (Id, SportId, Name, ShortName, IsPositive, CreatedAt) VALUES 
    (gen_random_uuid(), sport_id, 'Goal', 'GOAL', true, NOW()),
    (gen_random_uuid(), sport_id, 'Assist', 'ASST', true, NOW()),
    (gen_random_uuid(), sport_id, 'Sprint Won', 'SPR+', true, NOW()),
    (gen_random_uuid(), sport_id, 'Exclusion Earned', 'EXCL+', true, NOW()),
    (gen_random_uuid(), sport_id, 'Penalty Earned', 'PEN+', true, NOW()),
    (gen_random_uuid(), sport_id, 'Steal', 'STL', true, NOW()),
    (gen_random_uuid(), sport_id, 'Shot Saved', 'SAVE', true, NOW()),
    (gen_random_uuid(), sport_id, 'Block', 'BLK', true, NOW()),
    (gen_random_uuid(), sport_id, 'Shot Missed', 'MISS', false, NOW()),
    (gen_random_uuid(), sport_id, 'Turnover', 'T-OVER', false, NOW()),
    (gen_random_uuid(), sport_id, 'Exclusion Received', 'EXCL-', false, NOW()),
    (gen_random_uuid(), sport_id, 'Penalty Committed', 'PEN-', false, NOW()),
    (gen_random_uuid(), sport_id, 'Sprint Lost', 'SPR-', false, NOW()),
    (gen_random_uuid(), sport_id, 'Critical Foul', 'C-FOUL', false, NOW()),
    (gen_random_uuid(), sport_id, 'Bad Goal Conceded', 'B-GOAL', false, NOW()),
    (gen_random_uuid(), sport_id, 'Tactical Error', 'T-ERR', false, NOW()),
    (gen_random_uuid(), sport_id, 'Defensive Transition Failure', 'D-TRANS', false, NOW());
END $$;

-- ==========================================
-- 4. CLUBS & TEAMS
-- ==========================================

INSERT INTO Clubs (Id, CityId, Name, CreatedAt) VALUES 
(gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Lviv' LIMIT 1), 'Dynamo Lviv', NOW()),
(gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Mariupol' LIMIT 1), 'Mariupol', NOW()),
(gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Kharkiv' LIMIT 1), 'NTU-KhPI Kharkiv', NOW()),
(gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Lviv' LIMIT 1), 'KIVS-Levy Lviv', NOW()),
(gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Kharkiv' LIMIT 1), 'Kharkiv Oblast Team', NOW()),
(gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Uzhhorod' LIMIT 1), 'Zakarpattia Oblast Team', NOW()),
(gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Kyiv' LIMIT 1), 'Kyiv City Team', NOW()),
(gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Lviv' LIMIT 1), 'LFKS-Aquatico Lviv', NOW()),
(gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Lviv' LIMIT 1), 'Dynamo-Amazonky Lviv', NOW()),
(gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Kramatorsk' LIMIT 1), 'Donetsk Oblast Team', NOW());

INSERT INTO Teams (Id, ClubId, Name, CreatedAt) VALUES 
(gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Dynamo Lviv' LIMIT 1), 'Dynamo Lviv (Men)', NOW()),
(gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Mariupol' LIMIT 1), 'SHVSM Mariupol (Men)', NOW()),
(gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'NTU-KhPI Kharkiv' LIMIT 1), 'NTU-KhPI - SHVSM (Men)', NOW()),
(gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'KIVS-Levy Lviv' LIMIT 1), 'KIVS-Levy (Men)', NOW()),
(gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Kharkiv Oblast Team' LIMIT 1), 'Kharkiv Oblast Selection (Men)', NOW()),
(gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Zakarpattia Oblast Team' LIMIT 1), 'Zakarpattia Oblast - UzhNU (Men)', NOW()),
(gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Kyiv City Team' LIMIT 1), 'Kyiv City Selection (Men)', NOW()),
(gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'LFKS-Aquatico Lviv' LIMIT 1), 'LFKS-Aquatico (Men)', NOW()),
(gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Dynamo-Amazonky Lviv' LIMIT 1), 'Dynamo-Amazonky (Women)', NOW()),
(gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Donetsk Oblast Team' LIMIT 1), 'Donetsk Oblast Selection (Women)', NOW()),
(gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Kyiv City Team' LIMIT 1), 'Kyiv City Selection (Women)', NOW()),
(gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Kharkiv Oblast Team' LIMIT 1), 'Kharkiv Oblast Selection (Women)', NOW());