-- ==========================================
-- 1. GEOGRAPHY
-- ==========================================

DO $$ 
DECLARE 
    v_ukraine_id INT;
BEGIN
    INSERT INTO countries (name, code) 
    VALUES ('Ukraine', 'UKR')
    ON CONFLICT (code) DO NOTHING;

    SELECT id INTO v_ukraine_id FROM countries WHERE code = 'UKR';

    INSERT INTO regions (countryid, name) VALUES 
    (v_ukraine_id, 'Lviv Oblast'), 
    (v_ukraine_id, 'Kyiv City'), 
    (v_ukraine_id, 'Kyiv Oblast'), 
    (v_ukraine_id, 'Kharkiv Oblast'), 
    (v_ukraine_id, 'Dnipropetrovsk Oblast'), 
    (v_ukraine_id, 'Odessa Oblast'), 
    (v_ukraine_id, 'Donetsk Oblast'), 
    (v_ukraine_id, 'Zakarpattia Oblast')
    ON CONFLICT (countryid, name) DO NOTHING;

    INSERT INTO cities (id, regionid, name) VALUES 
    ('c0000000-0000-0000-0000-000000000001', (SELECT id FROM regions WHERE name = 'Lviv Oblast' AND countryid = v_ukraine_id), 'Lviv'),
    ('c0000000-0000-0000-0000-000000000002', (SELECT id FROM regions WHERE name = 'Kyiv City' AND countryid = v_ukraine_id), 'Kyiv'),
    ('c0000000-0000-0000-0000-000000000003', (SELECT id FROM regions WHERE name = 'Kyiv Oblast' AND countryid = v_ukraine_id), 'Brovary'),
    ('c0000000-0000-0000-0000-000000000004', (SELECT id FROM regions WHERE name = 'Kharkiv Oblast' AND countryid = v_ukraine_id), 'Kharkiv'),
    ('c0000000-0000-0000-0000-000000000005', (SELECT id FROM regions WHERE name = 'Dnipropetrovsk Oblast' AND countryid = v_ukraine_id), 'Dnipro'),
    ('c0000000-0000-0000-0000-000000000006', (SELECT id FROM regions WHERE name = 'Odessa Oblast' AND countryid = v_ukraine_id), 'Odessa'),
    ('c0000000-0000-0000-0000-000000000007', (SELECT id FROM regions WHERE name = 'Donetsk Oblast' AND countryid = v_ukraine_id), 'Mariupol'),
    ('c0000000-0000-0000-0000-000000000008', (SELECT id FROM regions WHERE name = 'Donetsk Oblast' AND countryid = v_ukraine_id), 'Kramatorsk'),
    ('c0000000-0000-0000-0000-000000000009', (SELECT id FROM regions WHERE name = 'Zakarpattia Oblast' AND countryid = v_ukraine_id), 'Uzhhorod')
    ON CONFLICT (regionid, name) DO NOTHING;

    RAISE NOTICE 'Geography seeding completed successfully.';
END $$;

-- ==========================================
-- 2. SPORT DEFINITION
-- ==========================================

DO $$ 
DECLARE 
    sport_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f5f';
BEGIN
    INSERT INTO sports (id, name) 
    VALUES (sport_id, 'Water Polo')
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO playerpositiondefinitions (id, sportid, name, shortname) VALUES 
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f61', sport_id, 'Goalkeeper', 'GK'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f62', sport_id, 'Center Forward', 'CF'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f63', sport_id, 'Center Back', 'CB'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f64', sport_id, 'Driver', 'D'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', sport_id, 'Wing', 'W'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f66', sport_id, 'Utility', 'UTL')
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit)
    VALUES ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f70', sport_id, true, 4, 8, '25x20m', 15, 13)
    ON CONFLICT (id) DO NOTHING;

-- ==========================================
-- 3. TECHNICAL & TACTICAL ACTIONS
-- ==========================================

    INSERT INTO eventdefinitions (id, sportid, name, shortname, ispositive, createdat) VALUES 
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
    ON CONFLICT (id) DO NOTHING;
END $$;

-- ==========================================
-- 4. CLUBS & TEAMS
-- ==========================================

INSERT INTO clubs (id, cityid, name, createdat) VALUES 
('11111111-1111-1111-1111-111111111101', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Lviv' AND r.name = 'Lviv Oblast' AND co.code = 'UKR' LIMIT 1), 'Dynamo Lviv', NOW()),
('11111111-1111-1111-1111-111111111102', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Mariupol' AND r.name = 'Donetsk Oblast' AND co.code = 'UKR' LIMIT 1), 'Mariupol', NOW()),
('11111111-1111-1111-1111-111111111103', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Kharkiv' AND r.name = 'Kharkiv Oblast' AND co.code = 'UKR' LIMIT 1), 'NTU-KhPI Kharkiv', NOW()),
('11111111-1111-1111-1111-111111111107', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Kyiv' AND r.name = 'Kyiv City' AND co.code = 'UKR' LIMIT 1), 'Kyiv City Team', NOW()),
('11111111-1111-1111-1111-111111111109', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Lviv' AND r.name = 'Lviv Oblast' AND co.code = 'UKR' LIMIT 1), 'Dynamo-Amazonky Lviv', NOW()),
('11111111-1111-1111-1111-111111111110', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Dnipro' AND r.name = 'Dnipropetrovsk Oblast' AND co.code = 'UKR' LIMIT 1), 'My super club', NOW())
ON CONFLICT (id) DO NOTHING;

DO $$ 
DECLARE 
    v_wp_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f5f';
BEGIN
    INSERT INTO teams (id, clubid, sportid, name, minbirthyear, gender, createdat) VALUES 
    ('22222222-2222-2222-2222-222222222201', '11111111-1111-1111-1111-111111111101', v_wp_id, 'Dynamo Lviv (Men)', NULL, 'Male', NOW()),
    ('22222222-2222-2222-2222-222222222202', '11111111-1111-1111-1111-111111111102', v_wp_id, 'SHVSM Mariupol (Men)', NULL, 'Male', NOW()),
    ('22222222-2222-2222-2222-222222222203', '11111111-1111-1111-1111-111111111103', v_wp_id, 'NTU-KhPI - SHVSM (Men)', NULL, 'Male', NOW()),
    ('22222222-2222-2222-2222-222222222213', '11111111-1111-1111-1111-111111111101', v_wp_id, 'Dynamo Lviv U-13 (2013)', 2013, 'Male', NOW()),
    ('22222222-2222-2222-2222-222222222209', '11111111-1111-1111-1111-111111111107', v_wp_id, 'Kyiv U-15 (2011)', 2011, 'Male', NOW()),
    ('22222222-2222-2222-2222-222222222204', '11111111-1111-1111-1111-111111111109', v_wp_id, 'Dynamo-Amazonky (Women)', NULL, 'Female', NOW()),
    ('22222222-2222-2222-2222-222222222211', '11111111-1111-1111-1111-111111111107', v_wp_id, 'Kyiv City Selection (Women)', NULL, 'Female', NOW()),
    ('22222222-2222-2222-2222-222222222215', '11111111-1111-1111-1111-111111111110', v_wp_id, 'My super team U-15 (2011)', 2011, 'Male', NOW())
    ON CONFLICT (id) DO NOTHING;
END $$;

-- ==========================================
-- 5. OAuth CHECK & PERMISSIONS
-- ==========================================

DO $$ 
DECLARE 
    v_user_id VARCHAR := 'auth0|698b956080889e5401cef7c5'; 
    v_team_id UUID := '22222222-2222-2222-2222-222222222201'; 
BEGIN
    INSERT INTO users (id, email, displayname, createdat)
    VALUES (v_user_id, 'user1@example.com', 'UserOne', NOW())
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO teammemberships (id, userid, teamid, roleinteam, joinedat, isprimary)
    SELECT '99999999-9999-9999-9999-999999999901', v_user_id, v_team_id, 'HeadCoach', NOW(), true
    WHERE NOT EXISTS (
        SELECT 1 FROM teammemberships 
        WHERE userid = v_user_id AND teamid = v_team_id
    );

    INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat)
    SELECT 
        '99999999-9999-9999-9999-999999999902', 
        v_user_id, 
        'Editor', 
        'Team', 
        v_team_id, 
        NOW()
    WHERE NOT EXISTS (
        SELECT 1 FROM auth.accesspolicies 
        WHERE userid = v_user_id 
          AND role = 'Editor' 
          AND targettype = 'Team' 
          AND targetid = v_team_id
    );

    RAISE NOTICE 'Seed completed: User % linked to Team %', v_user_id, v_team_id;
END $$;

-- ==========================================
-- 6. PLAYERS (Seed for My super club)
-- ==========================================

DO $$ 
DECLARE 
    v_club_id uuid := '11111111-1111-1111-1111-111111111110';
BEGIN
    INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat) VALUES 
    ('33333333-3333-3333-3333-333333333001', v_club_id, 'Олександр', 'Коваленко', '2011-05-15', 'Male', NOW()),
    ('33333333-3333-3333-3333-333333333002', v_club_id, 'Максим', 'Бондар', '2011-08-22', 'Male', NOW()),
    ('33333333-3333-3333-3333-333333333003', v_club_id, 'Артем', 'Шевченко', '2012-01-10', 'Male', NOW()),
    ('33333333-3333-3333-3333-333333333004', v_club_id, 'Дмитро', 'Марченко', '2011-11-30', 'Male', NOW()),
    ('33333333-3333-3333-3333-333333333005', v_club_id, 'Іван', 'Сидоренко', '2011-03-05', 'Male', NOW())
    ON CONFLICT (id) DO NOTHING;

    RAISE NOTICE 'Seed for 5 players in "My super club" completed successfully.';
END $$;