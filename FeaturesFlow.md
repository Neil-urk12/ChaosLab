# App flow

## System flow I think

### Web Unidirectional flow (with the assumption that user already downloaded the app)

1. Start app
2. Show login page (IF First login/initial login, show and allow default Super Admin credentials)
3. Enter new credentials (Require user to update credentials after login)
4. Override the default super admin credentials in the users table of the database with the new credentials
5. Authenticate the user.
6. Generate the authentication/session tokens in the backend.
7. Store the authentication token/session information securely in browser cookies.
8. Display dashboard page with CTAs
9. Show optional step-by-step overview of features that can be skipped
10. Create project 
11. Enter project info
12. Add Project Members
13. Assign roles/permissions to Project Members
14. Set the target API Base URL
15. Import an OpenAPI Spec
16. Parse the OpenAPI spec
17. Display discovered API endpoints, methods, parameters, schemas, and security requirements 
18. Configure auth credentials required by the target API
19. Validate the API Base URL and target API connectivity
20. Match the imported OepanAPI Spec against the target API config
21. Generate applicable chaos/failure scenarios
22. Display generated scenarios
23. Enter optional developer invariants
24. Configure the resilience test
25. Select endpoitns/scenarios to inculd in the test
26. Start the resilience test
27. Send test requests toward the target API throgh the ChaosLab testing/proxy layer
28. Inject configured failure conditions/primitives
29. Observe the target API/application responses
30. Record request, response, timing, failure, and recovery info
31. Evaluate detected resilience behavior
32. Determine whether retry, timeout, cricuit breaker, fallback, and related resilience mechanisms behaved as expected
33. Calcuate test results and resilience metrics
34. Generate the resilinece score
35. Display the test run results. 
36. Display failure timelines and scenario-level findings
37. Display recommendations
38. Save the completed test run and its results to the database
39. Allow the user to generate/export a PDF report
40. Retun to the Project Dashboard
41. User can run another test, modify api config, import/update openapi spec, review test history, manage members, or view exported stuff anything
42. User log out
43. Invalidate the active authentication session/tokens
44. Return to the login page

### Mobile App unidirectional system flow

1. Start mobile app
2. Check whether an authenticated session exists
3. If no valid session exists, display the Login page
4. User enters credentials 
5. Send credentials to the backend for authentication
6. Backend validates the creds
7. Generate auth/session tokens
8. Store auth information securely on the mobile device
9. Display the Mobile Dashboard
10. Retrieve projects accessible to the authenticated user
11. Display project list
12. User selects a project
13. Retrieve project info
14. Display project overview
15. Display configtured API info
16. Display recent Test runs
17. User selects a Test run
18. Retrieve test run results
19. Display test run results (Test status, resilience score, scenario results, failure timeline, detected resilience behavior, )
20. Display test run recommendations
21. Allow the user to view available reports
22. Allow the user to receive/view test completions
23. User areturns to the dashboard or selects another project
23. User logs out
24. Remove/invalidate the authenticated session.
25. Return to the login page

## Flow of different processes

### Initial Super Admin setup
1. Start the app
2. Check wether 

### Login

3. Show login page (IF First login/initial login, show and allow default Super Admin credentials)
4. Enter new credentials (Require user to update credentials after login)
5. Override the default super admin credentials in the users table of the database with the new credentials
6. Generate access token in the backend and store the access token in the browser cookies

### Logout

1. User clicks logout button
2. Send logout request to the backend
3. Invalidate/revoke the active session/refresh token 
4. Delete authenticateon cookies/local session info
5. Clear user-specific application state
6. Redirect to the Login page

### Importing of OpenAPI Spec
1. Set API Base url
2. Paste openapi spec json or enter url of openapi docs
2. 

### Project Creation

1. User selecs/clicks 'Create Project'
2. Display Project Creation form
3. User enters (project name, description, and other metadata)
4. Validate project info
5. If validation fails, display validation errors
6. If validation succeeds, create the project
7. Assign the creator as Project Owner
8. Save the project in the db
9. Display dialog to user to either add project members, configur target api, or import openapi spec

### Project Member Management

1. User opens Project Members
2. Retrieve current Project Members
3. Display Project members and assigned roles
4. User selects add member
5. Enter/select the user to add
6. Select the project role/permissions
7. Validate whether the user can be added
8. Check whether the user is already a member
9. If invalid, display error or reason
10. If not, create project membership
11. Assign selected role/permissionw
12. Save membership info
13. Refresh Project Members list

#### Removing member

1. User selects a project member
2. Select Remove member
3. Display confirmation dialog
4. Confirm removal
5. Validate whether the requesting user has permission to remove the member
6. Remove/deactivate Project membership
7. Refresh Project Members list

### API Target COnfiguration

1. User opens the project
2. Select API configuration
3. Enter API Base URL
4. Validate th URL format
5. Test connectivity and reachability of API
6. If unreachable, display connection failure and allow the user to edit the base url
7. If reachable, display successful connectivity status
8. Save the Base URL to the project configuration

### Importing OpenAPI Specification

1. User opens API Spec configuration
2. Choose an import method ( Paste OpenAPI JSON/YAML, upload the json/yaml file, or enter the url of an openapi document)
3. Submit the OpenAPI spec
4. Backend receives the specification
5. Determine whether the input is JSON or YAML
6. Parse the OpenAPI doc
7. Verify that the spec is supported
8. Validate the OpenAPI Structure
9. If parsing or validation fails > Return validation errors > Display errors to the user > Allow the user to correct/re-import the specification
10. If valid, extract API information
11. Extract (API metadata, servers/base URLs, Paths, Endpoints, HTTP Methods, parameters, request & response schemas, status codes, security schemas)
12. Save the imported OpenAPI Spec
13. Save parsed API metadata and endpoint information
14. Displayt the discovered endpoints to the user
15. Allow the user to review the imported API structure
16. Continue to authentication/API configuration

### Target API Authentication Configuration

1. Read security schemes declared by the imported OpenAPI Spec
2. Determine whether authentication is required
3. If authentication is not required, continue to API validation
4. If auth is rquired, display the required authentication configuration (Bearer token, cookie, etc)
5. Determine authentication type (Bearer token, API Key, Basic auth)
6. User supplies the required test credentials
7. Store credentials securely for the Project/Test configuration
8. Send a validation request to the target API
9. If authentication fails, display authentication error and allow the user to update crds
10. If auth succeeds, mark the confg as validatied
11. Continue to scenario generation

### Endpoint Discovery and Processing

1. Retrive the parsed OpenAPI Spec
2. Read each API path
3. Identify each supported HTTP op
4. Extract request requirements
5. Extract params
6. Extract request body params
7. Extract expected response schemas/status codes
8. Create/update Endpoint records (inserts/updates to db)
9. Link endpoints with the Project/API spec
10. Display discovered endpoints
11. Allow the user to include/exclude endpoints from testing

### Chaos Scenario Generation

1. User selects 'Generate Scenarios' after API spec import and configuration
2. Retreive parsed API endpoints
3. Analyze each endpoint
4. Determine which failure scenarios are applicable to each endpoint
5. Generate scenarios using supported chaos primitives
6. Generate applicable duplicate-request scenarios for idempotency evaluation
7. Exclude scenarios that are not applicable toa particular endpoint
8. Assign scenario configuration/default parameters
9. Svae generated scenarios
10. Associate each scenario with its corresponding target
11. Allow the user to (enable/disable scenarios, review scenario parameter, configure supported parameters)
12. Continue to Test Run configuration

### Test Run Configuration

1. User selects Create/Start Test run
2. Retreive following info (project, api config, endpoints, generated scenarios)
3. Verify API connectivity
4. Verify authentication configuration
5. Display test configuration
6. User selects endpoints to test
7. User selects scenarios to execute
8. Configure supported testing options
9. Vlaidate the Test Run config
10. If invalid, display configuration errors
11. If not, create test run record and insert to db
12. Set TEst Run status to Pending/Queued
13. Continue to Test Execution

### Resilience Test Execution

1. User clicks Execute Tests
2. Set test run status to Running
3. Select the first endpoint/scenario combination
4. Construct the required API request
5. Apply authentication credentials
6. Send baseline/control request when required
7. Route the test reuqest through the chaos injection/testing layer
8. Apply the configured failure condition
9. Send the request toward the target API
10. Observe target API/application behavior
11. Capture (req info, resp info, http status, resp time, errors/exceptions, timeout info, retry attempts, recovery behavior)
12. Store the scenario exeuction result
13. Determine whether more scenarios remain
14. If yes, execute next
15. If no, finish the test run
16. Set Test run status to completed or failed/partially completed if execution problems occured
17. Continue to Results evaluation
