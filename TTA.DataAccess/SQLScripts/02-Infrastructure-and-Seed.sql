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
    -- Fix: Explicitly target the UNIQUE(CountryId, Name) constraint
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

    -- Insert Cities
    -- Fix: Explicitly target the UNIQUE(RegionId, Name) constraint
    INSERT INTO Cities (Id, RegionId, Name) VALUES 
    (gen_random_uuid(), (SELECT Id FROM Regions WHERE Name = 'Lviv Oblast' AND CountryId = v_ukraine_id), 'Lviv'),
    (gen_random_uuid(), (SELECT Id FROM Regions WHERE Name = 'Kyiv City' AND CountryId = v_ukraine_id), 'Kyiv'),
    (gen_random_uuid(), (SELECT Id FROM Regions WHERE Name = 'Kyiv Oblast' AND CountryId = v_ukraine_id), 'Brovary'),
    (gen_random_uuid(), (SELECT Id FROM Regions WHERE Name = 'Kharkiv Oblast' AND CountryId = v_ukraine_id), 'Kharkiv'),
    (gen_random_uuid(), (SELECT Id FROM Regions WHERE Name = 'Dnipropetrovsk Oblast' AND CountryId = v_ukraine_id), 'Dnipro'),
    (gen_random_uuid(), (SELECT Id FROM Regions WHERE Name = 'Odessa Oblast' AND CountryId = v_ukraine_id), 'Odessa'),
    (gen_random_uuid(), (SELECT Id FROM Regions WHERE Name = 'Donetsk Oblast' AND CountryId = v_ukraine_id), 'Mariupol'),
    (gen_random_uuid(), (SELECT Id FROM Regions WHERE Name = 'Donetsk Oblast' AND CountryId = v_ukraine_id), 'Kramatorsk'),
    (gen_random_uuid(), (SELECT Id FROM Regions WHERE Name = 'Zakarpattia Oblast' AND CountryId = v_ukraine_id), 'Uzhhorod')
    ON CONFLICT (RegionId, Name) DO NOTHING;

    RAISE NOTICE 'Geography seeding for Ukraine completed successfully.';
END $$;

-- ==========================================
-- 2. SPORT DEFINITION
-- ==========================================

DO $$ 
DECLARE 
    sport_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f5f';
BEGIN
    -- Added ON CONFLICT for idempotency
    INSERT INTO Sports (Id, Name) 
    VALUES (sport_id, 'Water Polo')
    ON CONFLICT (Id) DO NOTHING;

    INSERT INTO PlayerPositionDefinitions (Id, SportId, Name, ShortName) VALUES 
    (gen_random_uuid(), sport_id, 'Goalkeeper', 'GK'),
    (gen_random_uuid(), sport_id, 'Center Forward', 'CF'),
    (gen_random_uuid(), sport_id, 'Center Back', 'CB'),
    (gen_random_uuid(), sport_id, 'Driver', 'D'),
    (gen_random_uuid(), sport_id, 'Wing', 'W'),
    (gen_random_uuid(), sport_id, 'Utility', 'UTL')
    ON CONFLICT DO NOTHING;

    INSERT INTO SportConfigurations (Id, SportId, UsesCleanTime, PeriodsCount, PeriodDurationMinutes, FieldSize, RosterLimit, LineupLimit)
    VALUES (gen_random_uuid(), sport_id, true, 4, 8, '25x20m', 15, 13)
    ON CONFLICT DO NOTHING;

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
    (gen_random_uuid(), sport_id, 'Defensive Transition Failure', 'D-TRANS', false, NOW())
    ON CONFLICT DO NOTHING;
END $$;

-- ==========================================
-- 4. CLUBS & TEAMS
-- ==========================================

-- Use subqueries to insert only if the club doesn't exist by name
INSERT INTO Clubs (Id, CityId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Lviv' LIMIT 1), 'Dynamo Lviv', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Clubs WHERE Name = 'Dynamo Lviv');

INSERT INTO Clubs (Id, CityId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Mariupol' LIMIT 1), 'Mariupol', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Clubs WHERE Name = 'Mariupol');

INSERT INTO Clubs (Id, CityId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Kharkiv' LIMIT 1), 'NTU-KhPI Kharkiv', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Clubs WHERE Name = 'NTU-KhPI Kharkiv');

INSERT INTO Clubs (Id, CityId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Lviv' LIMIT 1), 'KIVS-Levy Lviv', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Clubs WHERE Name = 'KIVS-Levy Lviv');

INSERT INTO Clubs (Id, CityId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Kharkiv' LIMIT 1), 'Kharkiv Oblast Team', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Clubs WHERE Name = 'Kharkiv Oblast Team');

INSERT INTO Clubs (Id, CityId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Uzhhorod' LIMIT 1), 'Zakarpattia Oblast Team', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Clubs WHERE Name = 'Zakarpattia Oblast Team');

INSERT INTO Clubs (Id, CityId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Kyiv' LIMIT 1), 'Kyiv City Team', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Clubs WHERE Name = 'Kyiv City Team');

INSERT INTO Clubs (Id, CityId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Lviv' LIMIT 1), 'LFKS-Aquatico Lviv', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Clubs WHERE Name = 'LFKS-Aquatico Lviv');

INSERT INTO Clubs (Id, CityId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Lviv' LIMIT 1), 'Dynamo-Amazonky Lviv', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Clubs WHERE Name = 'Dynamo-Amazonky Lviv');

INSERT INTO Clubs (Id, CityId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Cities WHERE Name = 'Kramatorsk' LIMIT 1), 'Donetsk Oblast Team', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Clubs WHERE Name = 'Donetsk Oblast Team');

-- Insert Teams only if they don't exist
INSERT INTO Teams (Id, ClubId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Dynamo Lviv' LIMIT 1), 'Dynamo Lviv (Men)', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Teams WHERE Name = 'Dynamo Lviv (Men)');

INSERT INTO Teams (Id, ClubId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Mariupol' LIMIT 1), 'SHVSM Mariupol (Men)', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Teams WHERE Name = 'SHVSM Mariupol (Men)');

INSERT INTO Teams (Id, ClubId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'NTU-KhPI Kharkiv' LIMIT 1), 'NTU-KhPI - SHVSM (Men)', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Teams WHERE Name = 'NTU-KhPI - SHVSM (Men)');

INSERT INTO Teams (Id, ClubId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'KIVS-Levy Lviv' LIMIT 1), 'KIVS-Levy (Men)', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Teams WHERE Name = 'KIVS-Levy (Men)');

INSERT INTO Teams (Id, ClubId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Kharkiv Oblast Team' LIMIT 1), 'Kharkiv Oblast Selection (Men)', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Teams WHERE Name = 'Kharkiv Oblast Selection (Men)');

INSERT INTO Teams (Id, ClubId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Zakarpattia Oblast Team' LIMIT 1), 'Zakarpattia Oblast - UzhNU (Men)', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Teams WHERE Name = 'Zakarpattia Oblast - UzhNU (Men)');

INSERT INTO Teams (Id, ClubId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Kyiv City Team' LIMIT 1), 'Kyiv City Selection (Men)', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Teams WHERE Name = 'Kyiv City Selection (Men)');

INSERT INTO Teams (Id, ClubId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'LFKS-Aquatico Lviv' LIMIT 1), 'LFKS-Aquatico (Men)', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Teams WHERE Name = 'LFKS-Aquatico (Men)');

INSERT INTO Teams (Id, ClubId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Dynamo-Amazonky Lviv' LIMIT 1), 'Dynamo-Amazonky (Women)', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Teams WHERE Name = 'Dynamo-Amazonky (Women)');

INSERT INTO Teams (Id, ClubId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Donetsk Oblast Team' LIMIT 1), 'Donetsk Oblast Selection (Women)', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Teams WHERE Name = 'Donetsk Oblast Selection (Women)');

INSERT INTO Teams (Id, ClubId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Kyiv City Team' LIMIT 1), 'Kyiv City Selection (Women)', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Teams WHERE Name = 'Kyiv City Selection (Women)');

INSERT INTO Teams (Id, ClubId, Name, CreatedAt)
SELECT gen_random_uuid(), (SELECT Id FROM Clubs WHERE Name = 'Kharkiv Oblast Team' LIMIT 1), 'Kharkiv Oblast Selection (Women)', NOW()
WHERE NOT EXISTS (SELECT 1 FROM Teams WHERE Name = 'Kharkiv Oblast Selection (Women)');

-- ==========================================
-- 5. OAuth CHECK
-- ==========================================

DO $$ 
DECLARE 
    -- FIXED: Using Auth0 Subject ID (sub) instead of email address
    v_user_id VARCHAR := 'auth0|698b9560880889e5401cef7c'; 
    v_club_id UUID;
    v_team_id UUID;
BEGIN
    -- 1. Create user in Users table using Auth0 Identity ID as PK
    -- Fix: Explicitly target the PK constraint (Id)
    INSERT INTO Users (Id, Email, DisplayName, CreatedAt)
    VALUES (v_user_id, 'user1@example.com', 'UserOne', NOW())
    ON CONFLICT (Id) DO NOTHING;

    -- 2. Retrieve existing IDs for Club and Team
    SELECT Id INTO v_club_id FROM Clubs WHERE Name = 'Dynamo Lviv' LIMIT 1;
    SELECT Id INTO v_team_id FROM Teams WHERE Name = 'Dynamo Lviv (Men)' LIMIT 1;

    -- 3. Associate user with the team (Membership)
    -- Using INSERT ... SELECT to prevent duplicates when UserId/TeamId already exists
    INSERT INTO TeamMemberships (Id, UserId, TeamId, RoleInTeam, JoinedAt, IsPrimary)
    SELECT gen_random_uuid(), v_user_id, v_team_id, 'HeadCoach', NOW(), true
    WHERE NOT EXISTS (
        SELECT 1 FROM TeamMemberships 
        WHERE UserId = v_user_id AND TeamId = v_team_id
    );

    -- 4. Define Access Policy (RBAC)
    -- Using INSERT ... SELECT to ensure idempotency for the business key
    INSERT INTO AccessPolicies (Id, UserId, Role, TargetType, TargetId, CreatedAt)
    SELECT 
        gen_random_uuid(), 
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