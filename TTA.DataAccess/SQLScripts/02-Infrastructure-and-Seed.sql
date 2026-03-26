-- ==========================================
-- 1. GEOGRAPHY
-- ==========================================

DO $$ 
DECLARE 
    v_ukraine_id INT;
BEGIN
    -- Insert Ukraine and store its ID for region mapping
    INSERT INTO Countries (Name, Code) 
    VALUES ('Ukraine', 'UKR')
    ON CONFLICT (Code) DO NOTHING;

    SELECT Id INTO v_ukraine_id FROM Countries WHERE Code = 'UKR';

    -- Insert Ukrainian Regions linked to Ukraine ID
    INSERT INTO Regions (CountryId, Name) VALUES 
    (v_ukraine_id, 'Lviv Oblast'), 
    (v_ukraine_id, 'Kyiv City'), 
    (v_ukraine_id, 'Kyiv Oblast'), 
    (v_ukraine_id, 'Kharkiv Oblast'), 
    (v_ukraine_id, 'Dnipropetrovsk Oblast'), 
    (v_ukraine_id, 'Odessa Oblast'), 
    (v_ukraine_id, 'Donetsk Oblast'), 
    (v_ukraine_id, 'Zakarpattia Oblast')
    ON CONFLICT (CountryId, Name) DO NOTHING;

    -- Insert Cities using fixed UUIDs and qualified region lookups
    INSERT INTO Cities (Id, RegionId, Name) VALUES 
    ('c0000000-0000-0000-0000-000000000001', (SELECT Id FROM Regions WHERE Name = 'Lviv Oblast' AND CountryId = v_ukraine_id), 'Lviv'),
    ('c0000000-0000-0000-0000-000000000002', (SELECT Id FROM Regions WHERE Name = 'Kyiv City' AND CountryId = v_ukraine_id), 'Kyiv'),
    ('c0000000-0000-0000-0000-000000000003', (SELECT Id FROM Regions WHERE Name = 'Kyiv Oblast' AND CountryId = v_ukraine_id), 'Brovary'),
    ('c0000000-0000-0000-0000-000000000004', (SELECT Id FROM Regions WHERE Name = 'Kharkiv Oblast' AND CountryId = v_ukraine_id), 'Kharkiv'),
    ('c0000000-0000-0000-0000-000000000005', (SELECT Id FROM Regions WHERE Name = 'Dnipropetrovsk Oblast' AND CountryId = v_ukraine_id), 'Dnipro'),
    ('c0000000-0000-0000-0000-000000000006', (SELECT Id FROM Regions WHERE Name = 'Odessa Oblast' AND CountryId = v_ukraine_id), 'Odessa'),
    ('c0000000-0000-0000-0000-000000000007', (SELECT Id FROM Regions WHERE Name = 'Donetsk Oblast' AND CountryId = v_ukraine_id), 'Mariupol'),
    ('c0000000-0000-0000-0000-000000000008', (SELECT Id FROM Regions WHERE Name = 'Donetsk Oblast' AND CountryId = v_ukraine_id), 'Kramatorsk'),
    ('c0000000-0000-0000-0000-000000000009', (SELECT Id FROM Regions WHERE Name = 'Zakarpattia Oblast' AND CountryId = v_ukraine_id), 'Uzhhorod')
    ON CONFLICT (Id) DO NOTHING;

    RAISE NOTICE 'Geography seeding for Ukraine completed successfully.';
END $$;

-- ==========================================
-- 2. SPORT DEFINITION
-- ==========================================

DO $$ 
DECLARE 
    sport_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f5f';
BEGIN
    INSERT INTO Sports (Id, Name) 
    VALUES (sport_id, 'Water Polo')
    ON CONFLICT (Id) DO NOTHING;

    INSERT INTO PlayerPositionDefinitions (Id, SportId, Name, ShortName) VALUES 
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f61', sport_id, 'Goalkeeper', 'GK'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f62', sport_id, 'Center Forward', 'CF'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f63', sport_id, 'Center Back', 'CB'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f64', sport_id, 'Driver', 'D'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', sport_id, 'Wing', 'W'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f66', sport_id, 'Utility', 'UTL')
    ON CONFLICT (Id) DO NOTHING;

    INSERT INTO SportConfigurations (Id, SportId, UsesCleanTime, PeriodsCount, PeriodDurationMinutes, FieldSize, RosterLimit, LineupLimit)
    VALUES ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f70', sport_id, true, 4, 8, '25x20m', 15, 13)
    ON CONFLICT (Id) DO NOTHING;

    -- 3. TECHNICAL & TACTICAL ACTIONS
    INSERT INTO EventDefinitions (Id, SportId, Name, ShortName, IsPositive, CreatedAt) VALUES 
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f81', sport_id, 'Goal', 'GOAL', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f82', sport_id, 'Assist', 'ASST', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f83', sport_id, 'Sprint Won', 'SPR+', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f84', sport_id, 'Exclusion Earned', 'EXCL+', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f85', sport_id, 'Penalty Earned', 'PEN+', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f86', sport_id, 'Steal', 'STL', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f87', sport_id, 'Shot Saved', 'SAVE', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f88', sport_id, 'Block', 'BLK', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f89', sport_id, 'Shot Missed', 'MISS', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f90', sport_id, 'Turnover', 'T-OVER', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f91', sport_id, 'Exclusion Received', 'EXCL-', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f92', sport_id, 'Penalty Committed', 'PEN-', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f93', sport_id, 'Sprint Lost', 'SPR-', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f94', sport_id, 'Critical Foul', 'C-FOUL', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f95', sport_id, 'Bad Goal Conceded', 'B-GOAL', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f96', sport_id, 'Tactical Error', 'T-ERR', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f97', sport_id, 'Defensive Transition Failure', 'D-TRANS', false, NOW())
    ON CONFLICT (Id) DO NOTHING;
END $$;

-- ==========================================
-- 4. CLUBS & TEAMS
-- ==========================================

-- Fix: CityId lookup is now deterministic by joining with Regions and Countries
INSERT INTO Clubs (Id, CityId, Name, CreatedAt) VALUES 
('11111111-1111-1111-1111-111111111101', 
    (SELECT c.Id FROM Cities c JOIN Regions r ON c.RegionId = r.Id JOIN Countries co ON r.CountryId = co.Id 
     WHERE c.Name = 'Lviv' AND r.Name = 'Lviv Oblast' AND co.Code = 'UKR' LIMIT 1), 'Dynamo Lviv', NOW()),
('11111111-1111-1111-1111-111111111102', 
    (SELECT c.Id FROM Cities c JOIN Regions r ON c.RegionId = r.Id JOIN Countries co ON r.CountryId = co.Id 
     WHERE c.Name = 'Mariupol' AND r.Name = 'Donetsk Oblast' AND co.Code = 'UKR' LIMIT 1), 'Mariupol', NOW()),
('11111111-1111-1111-1111-111111111103', 
    (SELECT c.Id FROM Cities c JOIN Regions r ON c.RegionId = r.Id JOIN Countries co ON r.CountryId = co.Id 
     WHERE c.Name = 'Kharkiv' AND r.Name = 'Kharkiv Oblast' AND co.Code = 'UKR' LIMIT 1), 'NTU-KhPI Kharkiv', NOW()),
('11111111-1111-1111-1111-111111111104', 
    (SELECT c.Id FROM Cities c JOIN Regions r ON c.RegionId = r.Id JOIN Countries co ON r.CountryId = co.Id 
     WHERE c.Name = 'Lviv' AND r.Name = 'Lviv Oblast' AND co.Code = 'UKR' LIMIT 1), 'KIVS-Levy Lviv', NOW()),
('11111111-1111-1111-1111-111111111105', 
    (SELECT c.Id FROM Cities c JOIN Regions r ON c.RegionId = r.Id JOIN Countries co ON r.CountryId = co.Id 
     WHERE c.Name = 'Kharkiv' AND r.Name = 'Kharkiv Oblast' AND co.Code = 'UKR' LIMIT 1), 'Kharkiv Oblast Team', NOW()),
('11111111-1111-1111-1111-111111111106', 
    (SELECT c.Id FROM Cities c JOIN Regions r ON c.RegionId = r.Id JOIN Countries co ON r.CountryId = co.Id 
     WHERE c.Name = 'Uzhhorod' AND r.Name = 'Zakarpattia Oblast' AND co.Code = 'UKR' LIMIT 1), 'Zakarpattia Oblast Team', NOW()),
('11111111-1111-1111-1111-111111111107', 
    (SELECT c.Id FROM Cities c JOIN Regions r ON c.RegionId = r.Id JOIN Countries co ON r.CountryId = co.Id 
     WHERE c.Name = 'Kyiv' AND r.Name = 'Kyiv City' AND co.Code = 'UKR' LIMIT 1), 'Kyiv City Team', NOW()),
('11111111-1111-1111-1111-111111111108', 
    (SELECT c.Id FROM Cities c JOIN Regions r ON c.RegionId = r.Id JOIN Countries co ON r.CountryId = co.Id 
     WHERE c.Name = 'Lviv' AND r.Name = 'Lviv Oblast' AND co.Code = 'UKR' LIMIT 1), 'LFKS-Aquatico Lviv', NOW()),
('11111111-1111-1111-1111-111111111109', 
    (SELECT c.Id FROM Cities c JOIN Regions r ON c.RegionId = r.Id JOIN Countries co ON r.CountryId = co.Id 
     WHERE c.Name = 'Lviv' AND r.Name = 'Lviv Oblast' AND co.Code = 'UKR' LIMIT 1), 'Dynamo-Amazonky Lviv', NOW()),
('11111111-1111-1111-1111-111111111110', 
    (SELECT c.Id FROM Cities c JOIN Regions r ON c.RegionId = r.Id JOIN Countries co ON r.CountryId = co.Id 
     WHERE c.Name = 'Kramatorsk' AND r.Name = 'Donetsk Oblast' AND co.Code = 'UKR' LIMIT 1), 'Donetsk Oblast Team', NOW())
ON CONFLICT (Id) DO NOTHING;

-- Teams insert using fixed UUIDs
INSERT INTO Teams (Id, ClubId, Name, CreatedAt) VALUES 
('22222222-2222-2222-2222-222222222201', '11111111-1111-1111-1111-111111111101', 'Dynamo Lviv (Men)', NOW()),
('22222222-2222-2222-2222-222222222202', '11111111-1111-1111-1111-111111111102', 'SHVSM Mariupol (Men)', NOW()),
('22222222-2222-2222-2222-222222222203', '11111111-1111-1111-1111-111111111103', 'NTU-KhPI - SHVSM (Men)', NOW()),
('22222222-2222-2222-2222-222222222204', '11111111-1111-1111-1111-111111111104', 'KIVS-Levy (Men)', NOW()),
('22222222-2222-2222-2222-222222222205', '11111111-1111-1111-1111-111111111105', 'Kharkiv Oblast Selection (Men)', NOW()),
('22222222-2222-2222-2222-222222222206', '11111111-1111-1111-1111-111111111106', 'Zakarpattia Oblast - UzhNU (Men)', NOW()),
('22222222-2222-2222-2222-222222222207', '11111111-1111-1111-1111-111111111107', 'Kyiv City Selection (Men)', NOW()),
('22222222-2222-2222-2222-222222222208', '11111111-1111-1111-1111-111111111108', 'LFKS-Aquatico (Men)', NOW()),
('22222222-2222-2222-2222-222222222209', '11111111-1111-1111-1111-111111111109', 'Dynamo-Amazonky (Women)', NOW()),
('22222222-2222-2222-2222-222222222210', '11111111-1111-1111-1111-111111111110', 'Donetsk Oblast Selection (Women)', NOW()),
('22222222-2222-2222-2222-222222222211', '11111111-1111-1111-1111-111111111107', 'Kyiv City Selection (Women)', NOW()),
('22222222-2222-2222-2222-222222222212', '11111111-1111-1111-1111-111111111105', 'Kharkiv Oblast Selection (Women)', NOW())
ON CONFLICT (Id) DO NOTHING;

-- ==========================================
-- 5. OAuth CHECK
-- ==========================================

DO $$ 
DECLARE 
    v_user_id VARCHAR := 'auth0|698b9560880889e5401cef7c'; 
    v_team_id UUID := '22222222-2222-2222-2222-222222222201'; -- Linked to 'Dynamo Lviv (Men)'
BEGIN
    -- 1. Create user in Users table
    INSERT INTO Users (Id, Email, DisplayName, CreatedAt)
    VALUES (v_user_id, 'user1@example.com', 'UserOne', NOW())
    ON CONFLICT (Id) DO NOTHING;

    -- 2. Associate user with the team (Membership)
    INSERT INTO TeamMemberships (Id, UserId, TeamId, RoleInTeam, JoinedAt, IsPrimary)
    SELECT '99999999-9999-9999-9999-999999999901', v_user_id, v_team_id, 'HeadCoach', NOW(), true
    WHERE NOT EXISTS (
        SELECT 1 FROM TeamMemberships 
        WHERE UserId = v_user_id AND TeamId = v_team_id
    );

    -- 3. Define Access Policy (RBAC)
    INSERT INTO AccessPolicies (Id, UserId, Role, TargetType, TargetId, CreatedAt)
    SELECT 
        '99999999-9999-9999-9999-999999999902', 
        v_user_id, 
        'Editor', 
        'Team', 
        v_team_id, 
        NOW()
    WHERE NOT EXISTS (
        SELECT 1 FROM AccessPolicies 
        WHERE UserId = v_user_id 
          AND Role = 'Editor' 
          AND TargetType = 'Team' 
          AND TargetId = v_team_id
    );

    RAISE NOTICE 'Seed completed: User % linked to Team %', v_user_id, v_team_id;
END $$;